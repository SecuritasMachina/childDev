#!/usr/bin/env python3
"""Promote an already-uploaded versionCode to a COMPLETED (full-rollout) production
release and commit — which submits it for review.

Uploading a bundle and setting a release to status="draft" leaves it parked in the
Console: it never reaches review or users. This flips that release to "completed".

Usage: play-promote.py <package> <sa-key-path> <versionCode> <versionName>
"""
import sys

from google.oauth2 import service_account
from googleapiclient.discovery import build
from googleapiclient.errors import HttpError

TRACK = "production"
RELEASE_NOTES = "Targets Android 16 (API 36) per the latest Google Play requirements."


def main(package: str, sa_key: str, version_code: str, version_name: str) -> None:
    creds = service_account.Credentials.from_service_account_file(
        sa_key, scopes=["https://www.googleapis.com/auth/androidpublisher"]
    )
    svc = build("androidpublisher", "v3", credentials=creds, cache_discovery=False)
    edits = svc.edits()

    edit_id = edits.insert(packageName=package, body={}).execute()["id"]
    print(f"edit: {edit_id}")

    edits.tracks().update(
        packageName=package,
        editId=edit_id,
        track=TRACK,
        body={
            "track": TRACK,
            "releases": [
                {
                    "name": version_name,
                    "versionCodes": [str(version_code)],
                    "status": "completed",
                    "releaseNotes": [{"language": "en-US", "text": RELEASE_NOTES}],
                }
            ],
        },
    ).execute()
    print(f"release {version_name} ({version_code}) set to COMPLETED on {TRACK}")

    try:
        res = edits.commit(packageName=package, editId=edit_id).execute()
        print(f"committed edit {res['id']} — SENT FOR REVIEW automatically")
    except HttpError as exc:
        if "changesNotSentForReview" not in str(exc):
            raise
        res = edits.commit(
            packageName=package, editId=edit_id, changesNotSentForReview=True
        ).execute()
        print(f"committed edit {res['id']} (staged — click 'Send for review' in the Console)")


if __name__ == "__main__":
    if len(sys.argv) != 5:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4])
