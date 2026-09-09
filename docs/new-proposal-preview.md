# New Proposal 2026 preview

This artifact is a build-only preview for Civil 3D 2026. It is not evidence
that proposal creation or the complete Phase 4 workflow has been qualified.

1. Download and extract `autogis-civil3d-preview-2026-<commit>`, using the
   exact commit you intend to check. Confirm that `build-info.json` names that
   commit and that its SHA-256 values match the two product DLLs.
2. On the licensed Civil 3D 2026 work computer, open a disposable drawing.
3. Run `NETLOAD` and select `AutoGIS.Civil3D.Adapter.dll` from the extracted
   artifact. Do not change `SECURELOAD`, `TRUSTEDPATHS`, account, or license
   settings to make the load succeed.
4. Run `SCRIPT` and select `new-proposal-smoke.scr`.
5. Confirm that `AUTOGISPROPOSALSMOKE` prints `PASS`, target 2026, and observed
   AutoCAD 25.1 and Civil 13.8 versions.
6. `AUTOGISNEWPROPOSAL` then opens the modal preview. Check the inputs and plan,
   and cancel it. Creation remains disabled and no project files should appear.

Record the artifact name and SHA-256, exact commit, Civil 3D release/update,
the smoke output, the modal/cancel result, and a before/after filesystem check
on the native preview issue. A smoke pass proves command and host binding only.
