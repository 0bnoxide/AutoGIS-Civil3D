#!/usr/bin/env python3
"""Poll a pull request's three comment surfaces and report new activity.

GitHub scatters review activity across three REST collections:

  * ``pulls/{n}/reviews``   — submitted reviews (APPROVE / REQUEST_CHANGES / COMMENT)
  * ``pulls/{n}/comments``  — inline review comments anchored to a diff line
  * ``issues/{n}/comments`` — the PR conversation timeline

A monitor that watches only one surface misses the others, and one that keys
events by their rendered location (``path:line``) re-reports every inline
comment when a push shifts the diff. This poller reads all three, dedups by
``(surface, id)`` — a stable identity that does not move — reports each new
event once, skips events authored by the running identity so it never echoes
its own comments, and prints ``POLL-FAIL`` (never silence) when a surface
cannot be read.

Ported as P3 of
``docs/superpowers/plans/2026-08-07-port-backlog-verified-gaps.md``.
Reimplemented in Python rather than porting the AutoGIS bash original: the
self-check then runs in the existing coordination unittest job with no
shell-in-CI and no Windows/bash dependency, and the fetch boundary is injected
so that self-check needs no live ``gh``.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from collections import OrderedDict
from typing import Callable, NamedTuple


class Surface(NamedTuple):
    kind: str
    path_template: str


# The three distinct surfaces. Dropping one is the "missing surface" regression
# the self-check guards against.
SURFACES: tuple[Surface, ...] = (
    Surface("review", "repos/{repo}/pulls/{n}/reviews"),
    Surface("comment", "repos/{repo}/pulls/{n}/comments"),
    Surface("issue-comment", "repos/{repo}/issues/{n}/comments"),
)


class PollFailure(Exception):
    """A surface could not be read. Surfaced as POLL-FAIL, never swallowed."""


# A Fetcher maps a gh api path to the decoded JSON array of items for that
# surface. Injected so the self-check drives the logic without a live gh.
Fetcher = Callable[[str], list]


class Event(NamedTuple):
    kind: str
    id: int
    login: str
    summary: str

    def key(self) -> tuple[str, int]:
        return (self.kind, self.id)

    def render(self) -> str:
        return f"{self.kind} {self.id} @{self.login}: {self.summary}"


def _login_of(item: dict) -> str:
    user = item.get("user") or {}
    return user.get("login") or ""


def _summarize(kind: str, item: dict) -> str:
    body = (item.get("body") or "").strip()
    first = body.splitlines()[0] if body else ""
    if not first and kind == "review":
        # An APPROVE/REQUEST_CHANGES review often carries no body.
        return item.get("state") or ""
    return first


def poll_once(
    repo: str,
    pr: int,
    seen: set,
    self_login: str,
    fetch: Fetcher,
) -> tuple[list[Event], list[str]]:
    """Read every surface once. Return ``(new events, POLL-FAIL messages)``.

    New events are those whose ``(surface, id)`` key has not been seen and whose
    author is not ``self_login``. ``seen`` is mutated to include every reported
    key. A surface whose fetch raises :class:`PollFailure` contributes a
    POLL-FAIL message and does not abort the remaining surfaces.
    """
    events: list[Event] = []
    failures: list[str] = []
    for surface in SURFACES:
        path = surface.path_template.format(repo=repo, n=pr)
        try:
            items = fetch(path)
        except PollFailure as exc:
            failures.append(f"POLL-FAIL {surface.kind} {path}: {exc}")
            continue
        for item in items:
            login = _login_of(item)
            if login == self_login:
                continue  # never echo our own activity
            event = Event(surface.kind, int(item["id"]), login,
                          _summarize(surface.kind, item))
            if event.key() in seen:
                continue
            seen.add(event.key())
            events.append(event)
    return events, failures


def gh_fetch(path: str) -> list:
    """Real fetcher: ``gh api --paginate --slurp`` across all pages.

    ``--slurp`` collects the paginated responses into one JSON array; each list
    endpoint yields an array per page, so flatten one level. A non-zero ``gh``
    exit becomes :class:`PollFailure` so the caller reports POLL-FAIL rather
    than mistaking the surface for empty.
    """
    try:
        proc = subprocess.run(
            ["gh", "api", "--paginate", "--slurp", path],
            capture_output=True, text=True, encoding="utf-8", errors="replace",
        )
    except OSError as exc:
        # gh absent or unlaunchable raises here, not a non-zero exit; poll_once
        # only catches PollFailure, so convert it or the monitor crashes.
        raise PollFailure(f"could not launch gh: {exc}") from exc
    if proc.returncode != 0:
        raise PollFailure(proc.stderr.strip() or f"gh exit {proc.returncode}")
    try:
        pages = json.loads(proc.stdout or "[]")
    except json.JSONDecodeError as exc:
        raise PollFailure(f"unparseable gh output: {exc}") from exc
    items: list = []
    for page in pages:
        if isinstance(page, list):
            items.extend(page)
        elif page:
            items.append(page)
    return items


def _gh_scalar(args: list[str]) -> str:
    try:
        proc = subprocess.run(
            ["gh", *args], capture_output=True, text=True,
            encoding="utf-8", errors="replace",
        )
    except OSError:
        return ""
    return proc.stdout.strip() if proc.returncode == 0 else ""


class _BoundedSeen:
    """Dedup key store with an LRU cap so a long-running watcher's memory stays
    bounded. The cap sits far above any real PR's event count, so eviction — and
    the one stale re-report it could cause — does not happen in practice. Offers
    the ``in`` / ``add`` interface poll_once expects, so a plain set works too.
    """

    def __init__(self, maxsize: int = 4096):
        self._keys: OrderedDict = OrderedDict()
        self._maxsize = maxsize

    def __contains__(self, key) -> bool:
        return key in self._keys

    def add(self, key) -> None:
        self._keys[key] = None
        while len(self._keys) > self._maxsize:
            self._keys.popitem(last=False)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Poll a PR's reviews, inline comments, and issue comments.")
    parser.add_argument("pr", type=int, help="pull request number")
    parser.add_argument("--repo", default=None,
                        help="owner/name (default: gh-resolved from the remote)")
    parser.add_argument("--self", dest="self_login", default=None,
                        help="identity to skip (default: authenticated gh user)")
    parser.add_argument("--interval", type=float, default=60.0,
                        help="seconds between polls (0 = poll once and exit)")
    args = parser.parse_args(argv)

    # gh returns UTF-8; on Windows a piped stdout defaults to the ANSI code
    # page, so an em-dash in a comment would crash the print. Pin UTF-8.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

    repo = args.repo or _gh_scalar(
        ["repo", "view", "--json", "nameWithOwner", "-q", ".nameWithOwner"])
    if not repo:
        print("POLL-FAIL: could not resolve repository (pass --repo)", flush=True)
        return 1
    if args.self_login is not None:
        self_login = args.self_login  # explicit; --self "" deliberately disables
    else:
        self_login = _gh_scalar(["api", "user", "-q", ".login"])
        if not self_login:
            # Without an identity the self-echo filter is inert and the monitor
            # would report its own comments — the defect this tool exists to fix.
            # Refuse rather than silently drop the guarantee.
            print("POLL-FAIL: could not resolve the authenticated gh identity; "
                  "pass --self <login> (or --self '' to disable the filter "
                  "deliberately)", flush=True)
            return 1

    seen = _BoundedSeen()
    poll_once_only = args.interval <= 0
    while True:
        events, failures = poll_once(repo, args.pr, seen, self_login, gh_fetch)
        for failure in failures:
            print(failure, flush=True)
        for event in events:
            print(event.render(), flush=True)
        if poll_once_only:
            return 1 if failures else 0
        time.sleep(args.interval)


if __name__ == "__main__":
    sys.exit(main())
