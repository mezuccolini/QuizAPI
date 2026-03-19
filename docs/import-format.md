# Import Format

This document describes the supported quiz import formats for `DevQuizAPI`.

## Supported Upload Types

- `.csv`
- `.txt`
- `.zip`

Use the admin page at [manage.html](../wwwroot/manage.html) to upload import files.

## CSV Columns

The import parser expects these column names:

- `QuizTitle` required
- `QuestionText` required
- `AnswerText` required
- `IsCorrect` required
- `Category` optional
- `QuestionImgKey` optional

### Column Meanings

- `QuizTitle`: groups rows into a quiz
- `QuestionText`: groups answer rows into a question
- `AnswerText`: the answer choice text for that question row
- `IsCorrect`: `true` or `false` for whether the answer is correct
- `Category`: optional quiz category label
- `QuestionImgKey`: optional image lookup key for ZIP package imports

## CSV Behavior

- Multiple rows with the same `QuizTitle` and `QuestionText` become one question with multiple answers.
- If more than one answer row for a question has `IsCorrect=true`, the question becomes a multi-select question.
- If a quiz with the same title and category already exists, the import replaces the existing quiz with the new one.

## Example CSV

```csv
QuizTitle,Category,QuestionText,AnswerText,IsCorrect,QuestionImgKey
Sample Networking Quiz,Networking,Which port does HTTPS use by default?,80,false,
Sample Networking Quiz,Networking,Which port does HTTPS use by default?,443,true,
Sample Networking Quiz,Networking,Identify the router interface shown.,GigabitEthernet0/0,true,router-diagram-01
Sample Networking Quiz,Networking,Identify the router interface shown.,Serial0/0/0,false,router-diagram-01
```

## ZIP Package Structure

A ZIP package should contain:

- exactly one CSV file at the package root
- optionally an `images/` folder containing question images

Example package layout:

```text
sample-quiz-package.zip
|-- sample-quiz.csv
`-- images
    |-- router-diagram-01.png
    `-- router-diagram-02.png
```

## How `QuestionImgKey` Works

`QuestionImgKey` links a CSV row to an image in the ZIP package.

Matching behavior:

- the value should match the image file name without the extension
- for `router-diagram-01`, the importer can match `images/router-diagram-01.png`
- all rows for the same question should use the same `QuestionImgKey` if that question has an image

Example:

- CSV value: `router-diagram-01`
- ZIP file: `images/router-diagram-01.png`

After import:

- the image file is extracted to `wwwroot/uploads/images/...`
- an image record is created in the database
- [quiz.html](../wwwroot/quiz.html) renders the question image automatically

## ZIP Upload Workflow

1. Upload the ZIP file in [manage.html](../wwwroot/manage.html).
2. The server extracts the CSV and images.
3. The admin page shows the extracted CSV file name.
4. Import that extracted CSV into the database.

ZIP files themselves are not imported directly into the quiz tables. The extracted CSV is the import source of truth.
