# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project follows semantic-style version tags.

## [Unreleased]

### Added

- Public user account management page
- Persisted signed-in quiz history with pass/fail tracking
- Public email verification flow support in the documented user path
- Quiz result home page for the most recent submitted attempt
- Rate limiting for auth-sensitive public endpoints and quiz loading
- Structured security logging for auth failures, admin actions, SMTP changes, and import failures

### Changed

- README now documents the public registration, account, and result-home flows
- FAQ now covers account history, signed-in quiz tracking, and the post-submit Home button flow
- Quiz runner now unlocks a `Home` button after submit and routes users into a result summary page
- Admin dashboard now restores only valid admin sessions and clears stale or unauthorized tokens automatically
- User-facing browser pages now surface cleaner API error messages
- Profile-oriented text inputs are now normalized to plain letters and spaces where appropriate

## [v0.1.0-alpha] - 2026-03-17

### Added

- Initial Git repository, README, and public sample quiz package
- Development sample data seeding for first-run local environments
- Browser landing page at `/`
- Swagger UI served from `/swapi.html`
- Integration test project covering auth, quiz, and import flows
- ZIP package image import and quiz image display support

### Changed

- Dev environment now uses automatic migrations on startup
- Upload/import UI now handles ZIP packages more clearly
- Uploaded file listing now reads from the correct private upload storage
- Quiz runner now relies on quiz/category selection instead of pasted quiz IDs
- Production configuration now fails fast when required secrets or connection settings are missing

### Removed

- Pre-employment quiz feature and related static pages from `DevQuizAPI`

### Fixed

- Login request media-type mismatch
- Dev admin seeding and development DB bootstrapping
- Missing quiz image associations during package import
- Uploaded ZIP visibility in the admin file list
- Landing page routing and root-path navigation issues

[v0.1.0-alpha]: https://github.com/mezuccolini/QuizAPI/releases/tag/v0.1.0-alpha
