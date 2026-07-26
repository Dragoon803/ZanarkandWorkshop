# GitHub upload checklist — v0.2.0

## Before tagging

- Complete the final manual Battle Script and in-game checks.
- Confirm the working tree contains only intended v0.2.0 changes.
- Commit and push the final source to the release branch.
- Confirm the GitHub Actions Build workflow passes for that commit.

## Create the draft release

Push the annotated version tag:

```powershell
git tag -a v0.2.0 -m "Zanarkand Workshop v0.2.0"
git push origin v0.2.0
```

The tag workflow will build the Windows package and create a draft GitHub
release containing:

- `ZanarkandWorkshop-v0.2.0-win-x64.zip`
- `ZanarkandWorkshop-v0.2.0-win-x64.zip.sha256`
- The curated notes from `RELEASE_NOTES_v0.2.0.md`

## Before publishing the draft

- Download the workflow-built ZIP and compare its checksum with the attached
  `.sha256` file.
- Launch `ZanarkandWorkshop.exe` from a newly extracted folder.
- Confirm the title bar and footer display `v0.2.0`.
- Confirm the README screenshots render on GitHub.
- Confirm the release targets tag `v0.2.0`.
- Publish the draft only after the final smoke and manual checks pass.
