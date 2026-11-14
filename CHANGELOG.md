# Changelog

## 0.1.0 — initial experimental public package

- Fixed core clock request up to 1800 MHz; separate adaptive range with 100–300 MHz minimum and up to 2200 MHz maximum, bounded by driver support.
- Optional offsets and power control only where supported.
- Explicitly confirmed startup profiles, bounded readiness probes and one apply attempt per sign-in.
- Owned-setting recovery, separate watchdog and persistent pause after failure.
- Accessible large controls and high-contrast theme.
- Fake-GPU and watchdog process tests.

Single-machine training observations are documented in README. This release does not establish a general TDR fix. Public packages include no saved profile, startup task, personal logs or certificates.
