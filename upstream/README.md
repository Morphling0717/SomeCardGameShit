# Upstream dependencies

Run `scripts/bootstrap-upstream.sh` from the repository root to clone the pinned client and core into `vendor/`.

The script refuses to overwrite a dirty checkout. Updating a revision requires editing `upstream.lock.json`, documenting why, and rerunning all core and Unity integration tests.

Do not copy assets from a released YGOPro2 package into this repository. Source-code licensing does not automatically grant rights to every bundled image, sound, font or third-party binary.
