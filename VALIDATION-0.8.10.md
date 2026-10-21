# 0.8.10 release verification

The 0.8.9 source was pushed but no v0.8.9 release/tag was published. The README still linked to the public 0.8.8 installer. This release updates the public installer and links together.

- Solution build: zero warnings/errors with SDK 10.0.401.
- 236 synthetic core regressions passed, including /v1 hosted-vLLM compatibility and model-name/URL/token routing.
- Extracted the generated installer into a new isolated directory. Installer, apphost, DLL and loaded runtime all report 0.8.10.
- Ran the actual extracted app: 16 model-management checks passed, including explicit edit/delete/import/export/save actions and runtime version. Screenshot inspected.
- Extracted-app long-conversation checks: 5,001 messages, at most 40 message views, older/latest access, retained message controls, consolidated prose controls.
- Previous-version cleanup fixtures: owned older versions removed; current/future/unowned directories, external history, unlisted user files preserved; locked old version remains intact.
- Normal installation validates the new app before cleaning the standard per-user previous installation directories. It requests graceful closure of an old Hermes app, updates the Start-menu shortcut and existing owned Desktop shortcuts. It does not force-kill running tasks or delete user data.
- Modified or locked previous app files produce an explicit incomplete-cleanup error instead of silently claiming success.

No real company endpoint or paid model was called. Tests use isolated fixture installations and synthetic keys. Actual Start-menu/Desktop changes are executed only by a normal user installation, not by isolated extraction tests.
