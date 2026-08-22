# Upstream dependencies

> **Historical reference only.** The YGOPro2/Unity route is no longer an active client target. These pins remain solely for legacy v1 compatibility tests and repository archaeology; the commands below reproduce the archived integration.

Run `scripts/bootstrap-upstream.sh` from the repository root to clone the pinned client and core into `vendor/`.

The script refuses to overwrite a dirty checkout. Updating a revision requires editing `upstream.lock.json`, documenting why, and rerunning all core and Unity integration tests.

Do not copy assets from a released YGOPro2 package into this repository. Source-code licensing does not automatically grant rights to every bundled image, sound, font or third-party binary.
