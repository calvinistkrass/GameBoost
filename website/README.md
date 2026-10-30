# GameBoost website

Static landing page (HTML/CSS/JS, no build step) for Vercel.

## Deploy on Vercel
- **Root Directory:** `website`
- **Framework Preset:** Other
- **Build Command / Output Directory:** leave empty

## Version & download link
`main.js` reads version, file size, date and download link automatically from the **latest GitHub release**
(the first `.zip` with `win-x64` in its name). Publishing a new release on GitHub is enough – no need to redeploy the website.

If there is no release yet or GitHub can't be reached, the `RELEASE` values at the top of `main.js` are used.

## Preview locally
```bash
python -m http.server 5173 --directory website
```
Then open http://localhost:5173.
