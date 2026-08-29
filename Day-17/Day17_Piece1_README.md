# Day 17 Piece 1 --- Deploy Angular to Azure Static Web Apps

## Objective

Deploy the Angular 21 `quotes-web` frontend to Azure Static Web Apps
using GitHub Actions CI/CD, connect it to the real Week-1 QuotesAPI, and
verify the live deployment.

The target was Managed Identity with no stored client secret. The
current SPA architecture does not provide a genuine server-side Managed
Identity token acquisition and API validation flow, so this remains a
documented limitation.

## 1. Brief to the Agent

I asked Claude Code to:

-   Inspect the existing Day-16 frontend and preserve working
    functionality.
-   Use the real Week-1 QuotesAPI.
-   Use real endpoints and fields only.
-   Configure Azure Static Web Apps.
-   Configure GitHub Actions CI/CD.
-   Keep the SWA deployment token only in the GitHub secret
    `AZURE_STATIC_WEB_APPS_API_TOKEN`.
-   Never store deployment tokens, client secrets, API keys, or access
    tokens in source code.
-   Deploy the Angular production output from
    `Day-17/quotes-web/dist/quotes-web/browser`.
-   Verify the live application, API, CORS, and Lighthouse.
-   Report Managed Identity honestly.

## 2. Azure Configuration

**Static Web App:** `swa-day17-quotesweb`

**Resource group:** `rg-day17-piece1-quotesweb`

**Region:** `eastasia`

**Live URL:**\
https://lemon-coast-0dd501000.7.azurestaticapps.net

**Production API:**\
https://quotes-api-final.proudpebble-45156de0.centralindia.azurecontainerapps.io

Real API endpoints:

``` text
GET /api/quotes?page=N&size=N
GET /api/quotes/{id}
```

Real quote fields:

``` text
id
author
text
```

## 3. CI/CD Configuration

Workflow:

``` text
.github/workflows/day17-quotes-web-swa.yml
```

The workflow:

1.  Checks out the repository.
2.  Sets up Node.js.
3.  Runs `npm ci`.
4.  Builds the Angular application.
5.  Runs Angular tests.
6.  Deploys the production output to Azure Static Web Apps.

Deployment configuration:

``` text
app_location: 'Day-17/quotes-web/dist/quotes-web/browser'
output_location: ''
skip_app_build: true
```

The deployment token is referenced only as:

``` text
${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN }}
```

No literal deployment token is stored in the repository.

## 4. Security Verification

The Day-17 source was scanned for:

-   deployment tokens
-   client secrets
-   API keys
-   subscription keys
-   access tokens
-   private keys

No literal deployment token or client secret was found.

A stray `lighthouse-report.json` file was removed before the Day-17
commit.

## 5. Real API Verification

### Quote list

Verified:

``` text
GET /api/quotes?page=1&size=10
```

The request succeeded and returned real quote data.

The UI displayed:

``` text
id
author
text
```

### Quote detail

Verified:

``` text
GET /api/quotes/{id}
```

using a real quote ID.

### Non-existent ID

Verified:

``` text
999999
```

The API returned `404`, and the frontend handled the missing quote
correctly.

## 6. CORS Verification

The actual SWA origin was verified:

``` text
https://lemon-coast-0dd501000.7.azurestaticapps.net
```

The real SWA origin received the correct CORS header.

An unauthorized test origin did not receive the CORS header.

CORS is therefore restricted to the real frontend origin rather than
using a wildcard.

## 7. GitHub Actions Verification

The workflow was manually triggered for:

``` text
day17-piece1
```

GitHub Actions run:

https://github.com/thinkbridge-thinkschool/thinkbridge-thinkschool-RahulYadav/actions/runs/33242301273

Result:

``` text
Success
```

Verified:

-   Checkout --- Success
-   Node.js setup --- Success
-   `npm ci` --- Success
-   Angular production build --- Success
-   Angular unit tests --- Success
-   Azure Static Web Apps deployment --- Success

The live application showed the updated build after deployment.

## 8. Automated Verification

**Angular tests:**

``` text
87/87 passed
```

**Production build:**

``` text
Passed
```

Production browser output:

``` text
Day-17/quotes-web/dist/quotes-web/browser
```

## 9. Lighthouse Verification

Lighthouse was run against the live production URL after deployment.

  Category           Score
  ---------------- -------
  Performance           99
  Accessibility        100
  Best Practices       100
  SEO                  100

Target:

``` text
>= 95
```

All four categories meet the target.

Browser console-errors audit:

``` text
0 errors
```

## 10. Live Verification Checklist

  Check                              Result
  ---------------------------------- -------------
  Live SWA loads                     Passed
  `/quotes`                          Passed
  `/quotes/1`                        Passed
  `GET /api/quotes?page=1&size=10`   Passed
  Real `id` field                    Passed
  Real `author` field                Passed
  Real `text` field                  Passed
  Quote detail endpoint              Passed
  Non-existent ID `999999`           404 handled
  CORS for actual SWA origin         Passed
  Unauthorized CORS origin           Blocked
  Browser console errors             0
  GitHub Actions                     Success
  Angular tests                      87/87
  Production build                   Passed
  Lighthouse Performance             99
  Lighthouse Accessibility           100
  Lighthouse Best Practices          100
  Lighthouse SEO                     100

## 11. Concrete Bug Caught

During the deployment review, the GitHub Actions workflow had an
incorrect deployment/build configuration.

It was corrected so the workflow uses the already-built Angular output:

``` text
Day-17/quotes-web/dist/quotes-web/browser
```

with:

``` text
skip_app_build: true
output_location: ''
```

The corrected workflow then completed successfully and deployed the
updated build.

## 12. Managed Identity Status

Managed Identity is **not complete** in the current SPA architecture.

There is no genuine server-side Managed Identity token acquisition and
API validation flow.

No client secret was added as a workaround.

A proper architecture would require a trusted server-side component,
such as a backend-for-frontend/API gateway, that:

1.  Uses an Azure Managed Identity.
2.  Acquires an access token for the API.
3.  Sends the token to the QuotesAPI.
4.  Has the QuotesAPI validate the token.
5.  Keeps identity credentials outside the browser.

Therefore the final status is:

``` text
Managed Identity: NOT COMPLETE
```

## 13. What Breaks If the API Contract Changes?

The frontend depends on the Week-1 API contract.

If the API base URL changes, the Angular production environment
configuration must be updated and rebuilt.

If:

``` text
GET /api/quotes?page=N&size=N
```

or its pagination parameters change, the `QuoteService`, state handling,
and tests may need updates.

If the quote fields change from:

``` text
id
author
text
```

to different names, the Angular model, templates, state logic, and tests
must be updated.

If:

``` text
GET /api/quotes/{id}
```

changes or is removed, the quote detail implementation and tests must be
updated.

If the API changes read endpoints to require authentication, the current
SPA would begin receiving `401 Unauthorized` responses unless a
supported token/authentication architecture is added.

## 14. Git Branch and Commit

Branch:

``` text
day17-piece1
```

Commit:

``` text
0054f02
```

The branch was pushed to the `thinkbridge` remote.

The workflow file was also registered on the default branch so GitHub
could expose `workflow_dispatch`. The Day-17 Piece 1 PR remained
unmerged during verification.

## 15. Final Status

-   [x] Angular deployed to Azure Static Web Apps
-   [x] GitHub Actions CI/CD succeeds
-   [x] Real Week-1 API works
-   [x] Real quote fields displayed
-   [x] Quote detail works
-   [x] Invalid quote ID handled
-   [x] CORS verified
-   [x] No browser console errors
-   [x] Lighthouse Performance 99
-   [x] Lighthouse Accessibility 100
-   [x] Lighthouse Best Practices 100
-   [x] Lighthouse SEO 100
-   [x] No deployment/client secret stored in source
-   [ ] Managed Identity --- not implemented in the current SPA
    architecture

The Managed Identity limitation should be reported honestly rather than
claiming that a browser SPA can directly acquire a managed-identity
token.
