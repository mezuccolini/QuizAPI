# FAQ

## Do I need a real database file from the repository?

No. The repository does not include a real database file.

For local development, the application creates and migrates the development database automatically when you run it in `Development`.

## What credentials should I use on first run?

In local development, the default seeded admin credentials are:

- email: `admin@quizapi.local`
- password: `Admin@123`

These are for local development only and should be changed for any shared environment.

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

## Why am I getting `415 Unsupported Media Type` on login?

The login endpoint expects form data in Swagger and browser form posts.

If you are calling the API yourself, use the format supported by the endpoint or follow the browser examples in [upload.html](../wwwroot/upload.html).

## Why does the app fail on startup in production?

Production startup fails fast if required configuration is missing.

Check:

- `ConnectionStrings__DefaultConnection`
- `Jwt__Key`
- `Jwt__Issuer`
- `Jwt__Audience`
- `Cors__AllowedOrigins__0`

See [DEPLOYMENT.md](../DEPLOYMENT.md) for the expected production setup.

## Where can I find the import format?

See [import-format.md](import-format.md).
