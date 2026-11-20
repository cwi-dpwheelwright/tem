# iOS CarPlay Release Checklist

## Signing and provisioning
- Confirm Apple Developer account access and team membership.
- Verify provisioning profiles for bundle id `com.cwi.personal`.
- Ensure signing certificate is valid and not expired.
- Confirm automatic signing or select the correct manual profile in Xcode.

## Build settings
- Set `CFBundleVersion` (build number) and `CFBundleShortVersionString` (marketing version).
- Validate `PRODUCT_BUNDLE_IDENTIFIER` matches the provisioning profile.
- Confirm `UIApplicationSceneManifest` includes CarPlay scene delegate.
- Ensure required Info.plist keys are present (pinning, crash reporter, simulation flags).

## App Store metadata
- Update display name, keywords, and description.
- Provide App Store screenshots (iPhone + CarPlay).
- Verify privacy policy URL and data usage declarations.

## Release validation
- Run unit tests and ensure all pass.
- Smoke test login + pack fetch + ask/action flows.
- Validate CarPlay tiles render and actions are gated in safe-mode.
- Verify kill-switch and survival mode banners appear correctly.

## Final verification (manual)
- Confirm signing/provisioning succeeds on a clean machine.
- Archive and validate in Xcode Organizer.
