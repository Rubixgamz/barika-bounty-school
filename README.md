# Barika Bounty

## Run locally

Run the app with `dotnet run --launch-profile http`. It serves the site at `http://localhost:5218`.

The local Development environment includes demonstration accounts for testing. Those accounts are not created in Production.

Individual accounts can register with email and password and sign in immediately. Organization registrations are saved as pending until an administrator activates the account in the Admin Portal.

Challenges can offer a cash bounty in KES, an employment opportunity, or both.

## Challenge categories

Built-in challenge categories include Health; Education; Environment; Agriculture & Food Systems; Arts, Culture & Heritage; Business & Entrepreneurship; Climate & Disaster Resilience; Community Development; Digital Technology & Data; Employment & Livelihoods; Energy; Finance & Financial Inclusion; Governance & Civic Engagement; Housing & Urban Development; Infrastructure & Public Works; Manufacturing & Industry; Public Safety & Justice; Science & Research; Social Protection & Inclusion; Sports & Recreation; Tourism & Hospitality; Transportation & Mobility; Water & Sanitation; Gender Equality & Women's Empowerment; and Youth & Child Development.

Signed-in users and organizations can suggest additional categories from their portals. Suggestions are private to the submitter until review; administrators can approve or reject them in the Admin Portal. Approval adds the category to challenge forms and filters. Existing saved categories and challenge assignments are retained.

Every proposal belongs to the challenge it was submitted for and inherits that challenge's category as its work area. The submission form shows the selected work area, and proposal lists display it for members, organizations, and administrators.

## Social sign-in

Google, LinkedIn, and GitHub buttons become available when that provider has both credentials configured on the server. Set credentials as environment variables; do not commit them:

| Provider | Client ID | Client secret | Local callback |
| --- | --- | --- | --- |
| Google | `GOOGLE_CLIENT_ID` | `GOOGLE_CLIENT_SECRET` | `http://localhost:5218/signin-google` |
| LinkedIn | `LINKEDIN_CLIENT_ID` | `LINKEDIN_CLIENT_SECRET` | `http://localhost:5218/signin-linkedin` |
| GitHub | `GITHUB_CLIENT_ID` | `GITHUB_CLIENT_SECRET` | `http://localhost:5218/signin-github` |

Register each callback URL with its provider. For a live deployment, use the matching HTTPS URL on the deployed domain and configure the same environment variables in the hosting provider. The provider must return a verified email address.

## Deploy the complete application to Render

The ASP.NET Core app serves both the website and API from one origin, so login, registration, challenges, proposals, and administration work together without a separate frontend host.

1. In Render, create a Blueprint and connect the public `Rubixgamz/barika-bounty-school` repository.
2. Apply `render.yaml`. Render builds the included `Dockerfile`, serves the complete app, and attaches a persistent disk for application records.
3. If starting with an empty data disk, set `BARIKA_ADMIN_EMAIL` and a strong `BARIKA_ADMIN_PASSWORD` (at least 12 characters) when prompted. Production creates this administrator on first startup and does not create local demonstration accounts. If you securely migrate an existing `barika-data.json` to the persistent disk before startup, that existing account data and password hashes are loaded unchanged.
4. Wait for the Render deployment to finish, then open the service URL. Confirm `/health` returns `{"status":"ok"}` and test registration, sign-in, challenge browsing, and proposal submission.

The Blueprint uses a paid Render service plan because the JSON repository needs a persistent disk. Do not remove the disk or use an ephemeral filesystem for real user data: records can otherwise be lost on restart or redeploy. The current JSON repository is intended for a single API instance; use a managed database and backups for a larger production service.

Never commit `App_Data/barika-data.json` or place it in the public source repository. It contains user records and password hashes. Keep a private backup and transfer it only through a secure channel if existing accounts need to be migrated; do not send account data or passwords in chat.

## Hosting and data

The app listens on `PORT` (default `8080`) when no ASP.NET URL is specified. `/health` is available for a host health check.

By default, records are stored in `App_Data/barika-data.json`. Set `BARIKA_DATA_FILE` to a path on persistent writable storage when deploying; an ephemeral container filesystem can lose records when the app restarts or redeploys.
