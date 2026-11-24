# Changelog

## 0.1.1 — Windows restart cleanup (24 November 2026)

- Fix normal Windows restart/sign-out being treated as an abnormal app exit and pausing confirmed startup.
- Handle session-end messages directly; complete owned-setting cleanup before returning control to Windows. Cancelled shutdown leaves the app running.
- Bound cleanup to two seconds. In-progress GPU operations, reset failures and timeouts retain the failure pause and recovery protection.
- Preserve startup preferences and confirmed profiles; never clear an existing failure pause automatically.
- Keep all GPU clock-setting code and limits unchanged.
- Pass 39 fake-GPU/process checks, including simulated Windows shutdown messages; original tester reports a successful real restart.

## 0.1.0 — initial experimental public package

- Fixed core clock request up to 1800 MHz; separate adaptive range with 100–300 MHz minimum and up to 2200 MHz maximum, bounded by driver support.
- Optional offsets and power control only where supported.
- Explicitly confirmed startup profiles, bounded readiness probes and one apply attempt per sign-in.
- Owned-setting recovery, separate watchdog and persistent pause after failure.
- Accessible large controls and high-contrast theme.
- Fake-GPU and watchdog process tests.

Single-machine training observations are documented in README. This release does not establish a general TDR fix. Public packages include no saved profile, startup task, personal logs or certificates.
