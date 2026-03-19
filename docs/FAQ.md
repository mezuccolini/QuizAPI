# FAQ

## Do I need a real database file from the repository?

No. The repository does not include a real database file.

For local development, the application creates and migrates the development database automatically when you run it in `Development`.

## What credentials should I use on first run?

In local development, the default seeded admin credentials are:

- email: `admin@quizapi.local`
- password: `Admin@123`

These are for local development only and should be changed for any shared environment.

## How do public users create an account?

Open [register.html](../wwwroot/register.html), create an account with your email address, and then confirm the verification link sent through SMTP.

After verification, sign in through [account.html](../wwwroot/account.html) or any page using the normal login flow.

## Where can I see my current pass / fail list of tests?

Sign in on [account.html](../wwwroot/account.html).

That page shows:

- total quiz attempts
- passed attempts
- failed attempts
- your current quiz history with pass/fail status, score, and completion time

Only signed-in quiz submissions are stored on your account.

## Why is my quiz not showing up in account history?

The most common cause is taking the quiz without being signed in.

Quiz-taking works anonymously, but anonymous submissions are not saved to a user account. To persist the result:

1. sign in first
2. take the quiz
3. press `Submit`

## What does the Home button do after I submit a quiz?

After `Submit`, the `Home` button becomes active in [quiz.html](../wwwroot/quiz.html).

It sends the user to [result-home.html](../wwwroot/result-home.html), which shows:

- the most recent quiz taken
- pass or fail status
- score
- correct answer count

That page also includes a centered `Select New Quiz` button to return to quiz selection quickly.

## Why does Swagger ask me for a bearer token?

Protected endpoints require JWT authentication.

Use `POST /api/auth/login` first, then paste the returned token into Swagger's `Authorize` dialog as:

```text
Bearer YOUR_TOKEN_HERE
```

## Why does uploading a ZIP not import the quiz immediately?

ZIP uploads are a two-step workflow:

1. upload the ZIP package
2. import the extracted CSV file that the admin page shows after extraction

The extracted CSV is the source of truth for the quiz import.

## Why are my images not showing up in the quiz runner?

Common causes:

- the ZIP package does not contain an `images/` folder
- `QuestionImgKey` does not match the image file name
- the quiz was imported before image support was added and needs to be re-imported

## Why am I getting `401 Unauthorized` in Swagger?

The endpoint is protected and Swagger is not yet sending your token.

Log in first, copy the JWT, then authorize with `Bearer <token>`.

## Why am I seeing `429 Too Many Requests`?

The application now rate-limits repeated requests on public auth-sensitive endpoints and quiz-loading endpoints.

If you hit `429`, wait a moment and try again instead of rapidly retrying the same request.

## Why am I getting `415 Unsupported Media Type` on login?

The login endpoint expects form data in Swagger and browser form posts.

If you are calling the API yourself, use the format supported by the endpoint or follow the browser examples in [manage.html](../wwwroot/manage.html).

## Why does the app fail on startup in production?

Production startup fails fast if required configuration is missing.

Check:

- `ConnectionStrings__DefaultConnection`
- `Jwt__Key`
- `Jwt__Issuer`
- `Jwt__Audience`
- `Cors__AllowedOrigins__0`
- `PublicApp__BaseUrl`

See [DEPLOYMENT.md](../DEPLOYMENT.md) for the expected production setup.

## Why did the admin dashboard sign me out automatically?

[manage.html](../wwwroot/manage.html) now verifies that the saved token is still valid and still belongs to an `Admin` user.

If the token has expired or the account is not an admin, the page clears the saved session and sends you back to the admin login form with a clearer message.

## Which text inputs are restricted to normal text now?

Profile-oriented name fields are normalized to letters and spaces only:

- public registration first name / last name
- admin-created and admin-edited user first name / last name
- SMTP `From Name`

Passwords and email addresses keep their normal required character flexibility, and imported quiz content is intentionally left more permissive because quiz text often needs punctuation.

## Where can I find the import format?

See [import-format.md](import-format.md).
