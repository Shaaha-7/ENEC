# Deploying the Qandeel dashboard

## Put the dashboard online (Streamlit Community Cloud, free)

The repo must be **public** (or your GitHub account must give Streamlit access).

1. Go to [share.streamlit.io](https://share.streamlit.io) and sign in with GitHub.
2. Click **Create app** → **Deploy a public app from GitHub**.
3. Repository `Shaaha-7/ENEC`, branch `main`, main file path `qandeel/dashboard.py`.
4. Optional: set the app URL, e.g. `qandeel`. Under **Advanced settings** choose Python 3.11.
5. Click **Deploy**. The first start takes a few minutes; after that every push to `main` updates the app.

The online app downloads a fresh 7-day forecast by itself (at most every 6 hours), so
**Live forecast** mode works without any setup. Demo link example:
`https://<your-app>.streamlit.app/?d=15&b=330&t=30`.

