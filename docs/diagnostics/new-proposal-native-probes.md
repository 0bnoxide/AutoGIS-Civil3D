# New Proposal native probes: Civil 3D 2026

This is the Task 3 decision record for [issue #106](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/106). It records one disposable Sheet Set experiment in Civil 3D 2026 on 2026-09-25 (local time). The [implementation plan](../superpowers/plans/2026-09-04-new-proposal-implementation-plan.md#task-3-prove-native-operations-before-committing-their-implementation) requires separate probes before the related production code and final qualification.

The [sanitized native run evidence](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/106#issuecomment-5843135341) records the pre-move hashes, relocated readback, and Sheet Set Manager check.

## Decision

**Proceed with a bounded Sheet Set implementation experiment.** In this host, in-process `AcSmComponents` COM created a DST with one sheet and one sheet-set custom property. A close and reopen returned the committed values. After the scratch folder moved, the DST resolved the moved DWG and its `Layout1`. Civil 3D's Sheet Set Manager listed the sheet and opened that layout. The DWG and DST hashes remained unchanged through the relocation checks and UI inspection.

This establishes feasibility for this one adjacent DST/DWG fixture. It does not qualify production Sheet Set integration, approved templates, title-block fields, Xrefs, data shortcuts, or general project portability. The [open owner inputs](../superpowers/specs/2026-09-04-civil-production-accelerator-design.md#open-owner-inputs), including the organization's Sheet Set Manager mode and approved DWTs, still gate company-specific creation.

## Host and reference provenance

| Item | Observed value |
| --- | --- |
| Host | Civil 3D 2026, AutoCAD 25.1, x64 process |
| Probe | Disposable `net8.0-windows`, x64 NETLOAD assembly with `Microsoft.WindowsDesktop.App.WPF` framework reference; no production code or package added |
| Managed references | Installed AutoCAD 2026 `AcCoreMgd.dll`, `AcDbMgd.dll`, `AcMgd.dll`, and `AcSmComponents.Interop.dll`; all `Private=false` |
| Sheet Set interop | Installed `AcSmComponents.Interop.dll` file version `25.1.164.0.0`; loaded assembly version `25.1.0.0` |
| Other installed components | `AcSmComponents.dll` and `AcSmComponents25.tlb`, both file version `25.1.164.0.0` |
| Scratch input | Civil 3D QNEW/QSAVE DWG, header `AC1032`, with native `Layout1`; no approved company template |

Autodesk documents [`AcSmSheetSetMgr`, `CreateDatabase`, `OpenDatabase`, `LockDb`, and `UnlockDb`](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-ActiveX-SSO/files/GUID-A72E5F7E-D18F-4171-8D30-58FEFCC76E03.htm), [the multi-client locking and object-lifetime rules](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-ActiveX-SSO/files/GUID-6D7E2FAD-BA29-4123-B4DF-52F9EE6E6AA7.htm), and [the `AcSmComponents25.tlb` migration for AutoCAD 2026](https://help.autodesk.com/cloudhelp/2026/FRA/AutoCAD-ActiveX/files/GUID-FF023966-A01D-4B64-8957-7C0F02BF8162.htm). The local probe referenced the installed interop assembly; it did not copy Autodesk binaries into the repository. A legal redistributable reference source for a generic CI runner has not been established. Native builds should use an Autodesk-equipped runner until that question is resolved.

## Hypotheses and procedure

The three pass conditions below were written before NETLOAD. The probe was compiled into a scratch directory, then loaded into the running host. Its assembly SHA-256 was `6CD994BAFB4859DAE1691783F7D4247B1AFA14F5CB102595066C7AA6D5978983`; the reviewed C# source SHA-256 was `BBEE681D3A68E4FF7168B13930F5A8358C810AACFF3A4FAA484D52F0166AE2EC`. Raw source, JSON reports, and scratch artifacts remain in local temporary storage; only this sanitized procedure and decision are committed.

| Hypothesis | Native action and observation | Result |
| --- | --- | --- |
| COM binding works in the target host | `AUTOGIS106BIND` instantiated `AcSmSheetSetMgrClass`; report returned `comActivated: true`, interop/managed assembly versions `25.1.0.0`, x64. | Pass |
| One sheet and property survive commit, close, and reopen | `AUTOGIS106ROUNDTRIP` called `CreateDatabase`, `LockDb`, `GetSheetSet`, `GetCustomPropertyBag`, `SetProperty`, `ImportSheet`, `InsertComponent`, `SetNumber`, `SetTitle`, `UnlockDb(commit: true)`, and `Close`. `OpenDatabase` then read back one sheet, `P-001`, `Probe Layout1`, `Layout1`, and the `CUSTOM_SHEETSET_PROP` value `round-trip-2026`. | Pass |
| The adjacent sheet reference survives a parent-folder move | After the first DST inspection closed, the entire scratch workspace moved to a sibling folder; its original path was absent. `AUTOGIS106VERIFYRELOCATED` reopened the moved DST, resolved its sheet reference to the moved DWG, and read `Layout1` from that DWG. Hashes were unchanged before/after inspection and after close. Sheet Set Manager then displayed `P-001 - Probe Layout1` and opened the moved DWG at `Layout1`; both scratch resources were closed afterward. | Pass for this fixture |

The DWG SHA-256 stayed `B7EEB65E9FD671FD178B50D8A2C9BD1C2B917EA818D1FF4D422F88F003F278AC`. The DST was 3,216 bytes and its SHA-256 stayed `736539FDB9063775D137351D3AB3C5828191BEB7EB13FF260D0473FF01E42BAD` across the folder move, relocated API inspection, and UI inspection. A same-byte `ProbeSheet.ds$` sidecar appeared after Sheet Set Manager inspection; neither primary file changed. The scratch DWG was closed without edits and the sheet set was closed in the palette.

[`OpenDatabase(path, true)`](https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-ActiveX/files/GUID-7EA3FFF8-FCE3-4B06-A66F-52B039D41816.htm) uses `true` for **fail if already open**, not for read-only access. The relocated verification made no mutation calls, and unchanged post-close hashes support the narrower nonmutation observation. The move succeeded after the probe's COM close cycle, but that does not independently prove every possible OS handle was released.

## Remaining Task 3 evidence

The [required spikes](../superpowers/specs/2026-09-04-civil-production-accelerator-design.md#implementation-boundary) still need native observations for updating an existing DST property after reopen, DWT-derived sheet DWGs, title-block field refresh from DST properties, relative overlay Xrefs across relocation, data-shortcut association, and the complete close/verify/close lifecycle for a multi-drawing staging root. The [receipt lifecycle and fault-injection gate](../superpowers/plans/2026-09-04-new-proposal-implementation-plan.md#task-3-prove-native-operations-before-committing-their-implementation) has [separate evidence on issue #101](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/101#issuecomment-5740833534); this probe adds no receipt evidence. This run did not inject native API failures or test a fresh host process, multiple sheets, nested folders, UNC paths, or another machine. Both returned layout paths were absolute, so the result does not establish how paths are serialized or broad portability. Company compatibility and final qualification require the owner's approved standards manifest and templates.
