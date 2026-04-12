# Beta Rollout: Linux x64 Server Validation

## Goal

Publish a beta container image for the current C# implementation and validate IronOCR end-to-end on a Linux x64 server before stable promotion.

## Recommended Beta Tag

Use a semver pre-release tag:

- `v2.0.0-beta.1`

This works with the current GitHub Actions tag trigger (`v*.*.*`) and Docker metadata setup.

## Preflight (Local)

Run from repository root.

1. Ensure tests pass:

```sh
dotnet test --configuration Release --nologo
```

2. Ensure no crash dumps or oversized debug artifacts are about to be committed:

```sh
find src -maxdepth 3 \( -name "*.core" -o -name "qemu_dotnet_*" \) -print
```

If any files are listed and you do not want them in git, remove them before commit.

3. Review pending changes:

```sh
git status --short
```

## Publish Beta

1. Commit the current implementation:

```sh
git add -A
git commit -m "release: prep v2.0.0-beta.1"
```

2. Create and push beta tag:

```sh
git tag v2.0.0-beta.1
git push origin master
git push origin v2.0.0-beta.1
```

3. Monitor image build/push workflow in GitHub Actions until green.

## Deploy On Linux x64 Server (IronOCR)

1. Pull the beta image:

```sh
docker pull michielvanbeers/notion-auto-ocr:v2.0.0-beta.1
```

2. Start beta container:

```sh
docker run -d \
  --name notion-auto-ocr-beta-ironocr \
  --restart unless-stopped \
  -e NOTION_TOKEN="<your_notion_token>" \
  -e DATABASE_ID="<your_database_id>" \
  -e SCAN_METHOD="checkbox" \
  -e OCR_PROVIDER="ironocr" \
  -e IRONOCR_LICENSE_KEY="<your_ironocr_key>" \
  -e DEBUG="true" \
  michielvanbeers/notion-auto-ocr:v2.0.0-beta.1
```

3. Watch logs:

```sh
docker logs -f notion-auto-ocr-beta-ironocr
```

## Smoke Test Checklist (Server)

Run a controlled test set (3 to 5 pages with OCR markers):

- Container starts without IronOCR native initialization errors.
- Pages are discovered correctly from Notion.
- OCR text is extracted and written back correctly.
- No crash/restart loop for at least 30 to 60 minutes.
- Logs do not show repeated OCR exceptions.

## Optional Azure Regression Check

Run a second container with `OCR_PROVIDER=azure` on the same beta image and validate one short scan pass.

## Go/No-Go Criteria

Promote if all are true:

- Server smoke test passes.
- No critical errors in logs.
- Azure path still works.

Do not promote if any are true:

- IronOCR startup/runtime errors persist on Linux x64.
- OCR output is incorrect or not written back.
- Container stability issues appear under normal scan cadence.

## Rollback

If beta fails:

```sh
docker rm -f notion-auto-ocr-beta-ironocr
# Then redeploy previous known-good image tag
```

Keep the failed beta logs for comparison and potential vendor escalation.
