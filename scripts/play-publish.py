#!/usr/bin/env python3
"""Upload the signed LevelUp AAB to Google Play (androidpublisher v3) as a STAGED
DRAFT on the production track (changesNotSentForReview=True — click "Send for
review" in the Play Console to submit).

Usage: play-publish.py <path-to-aab> <versionName for the release name>
Needs host pip packages google-api-python-client + google-auth.
"""
import sys

from google.oauth2 import service_account
from googleapiclient.discovery import build
from googleapiclient.errors import HttpError
from googleapiclient.http import MediaFileUpload

PACKAGE = "levelup.securitasmachina.org"
SA_KEY = "/home/jaxtrx/data/.secrets/playstoreapps-500719-5d7f8f26b138.json"
TRACK = "production"


def main(aab_path: str, version_name: str) -> None:
    creds = service_account.Credentials.from_service_account_file(
        SA_KEY, scopes=["https://www.googleapis.com/auth/androidpublisher"]
    )
    svc = build("androidpublisher", "v3", credentials=creds, cache_discovery=False)
    edits = svc.edits()

    edit_id = edits.insert(packageName=PACKAGE, body={}).execute()["id"]
    print(f"edit: {edit_id}")

    media = MediaFileUpload(aab_path, mimetype="application/octet-stream", resumable=True)
    bundle = edits.bundles().upload(
        packageName=PACKAGE, editId=edit_id, media_body=media
    ).execute()
    version_code = bundle["versionCode"]
    print(f"uploaded bundle versionCode={version_code}")

    edits.tracks().update(
        packageName=PACKAGE,
        editId=edit_id,
        track=TRACK,
        body={
            "track": TRACK,
            "releases": [
                {
                    "name": version_name,
                    "versionCodes": [str(version_code)],
                    "status": "completed",
                    "releaseNotes": [
                        {
                            "language": "en-US",
                            "text": "Targets Android 16 (API 36) per the latest Play requirements.",
                        }
                    ],
                }
            ],
        },
    ).execute()
    print(f"track {TRACK} updated (draft release {version_name})")

    # Play flips which commit mode it accepts depending on the app's review state; try
    # auto-send first, fall back to a staged draft.
    try:
        res = edits.commit(packageName=PACKAGE, editId=edit_id).execute()
        print(f"committed edit {res['id']} — SENT FOR REVIEW automatically")
    except HttpError as exc:
        if "changesNotSentForReview" not in str(exc):
            raise
        res = edits.commit(
            packageName=PACKAGE, editId=edit_id, changesNotSentForReview=True
        ).execute()
        print(f"committed edit {res['id']} (staged draft — click 'Send for review' in the Console)")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
