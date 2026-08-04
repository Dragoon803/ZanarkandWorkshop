# GitHub release checklist — v0.4.0

## Before tagging

- Confirm the working tree contains only intended v0.4.0 files.
- Build the solution in Release configuration.
- Run the offline smoke tests, including clean-master battlefield and treasure checks.
- Confirm the README and screenshots render correctly.
- Commit and push the final source to `main`.
- Confirm the GitHub Actions Build workflow passes.

## Create the draft release

```powershell
git tag -a v0.4.0 -m "Zanarkand Workshop v0.4.0"
git push origin v0.4.0
```

The tag workflow creates a draft containing:

- `ZanarkandWorkshop-v0.4.0-win-x64.zip`

The release description is taken from `RELEASE_NOTES_v0.4.0.md`.

## Before publishing

- Download the ZIP and confirm the application starts correctly.
- Extract the ZIP into a clean folder and launch the application.
- Confirm v0.4.0 is displayed and open a test master project.
- Test Save and recovery in the new editors.
- Publish the draft only after the packaged application passes.
