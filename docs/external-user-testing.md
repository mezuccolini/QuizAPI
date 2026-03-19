# External User Testing Guide

Use this guide when the app is ready for outside testers who are not part of day-to-day development.

## Goal

The purpose of this pass is to confirm that a new user can:

- register
- verify email
- sign in
- recover from a forgotten password
- take a quiz
- submit a quiz
- understand the result

without needing a developer to explain each step.

## Suggested Test Group

- 3 to 5 outside testers
- at least 1 tester using the app for the first time with no live guidance
- at least 1 tester focused on the admin/import path

## Tester Scenarios

### Public User

1. Open `/`
2. Register a new account
3. Use the verification email link
4. Sign in
5. Start a single-category quiz
6. Start a mixed-category quiz
7. Submit at least one quiz
8. Confirm the result page and account history make sense
9. Use password reset and confirm the email link works

### Guest User

1. Open `/quiz.html`
2. Take quizzes without signing in
3. Confirm the 5-question guest rule works
4. Confirm the 3-attempt limit works
5. Confirm the register call-to-action is obvious when the limit is reached

### Admin User

1. Open `/manage.html`
2. Sign in with an admin account
3. Upload and import the sample ZIP package
4. Verify imported quiz content appears in the quiz runner
5. Create a user
6. Edit the user's first and last name
7. Send a password reset link
8. Verify SMTP settings save and test correctly

## What To Capture

Ask each tester to report:

- anything confusing
- any step where they hesitated
- any message that felt technical or unclear
- any bug that blocked completion
- browser and device used

## Pass Criteria

You can reasonably call the app `beta` when:

- most testers complete the primary flow without help
- no critical auth, reset, import, or quiz-submission bugs appear
- feedback is mostly about polish, not broken functionality

## Operator Prep

Before sending the app to outside testers:

- run [post-deploy-smoke-test.ps1](../scripts/post-deploy-smoke-test.ps1)
- confirm `/health/live`, `/health/ready`, and `/version`
- confirm SMTP is configured
- confirm one admin account is available
- confirm the database is on the intended environment
