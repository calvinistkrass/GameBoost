// GameBoost website: version and download links.
// The values below are the fallback. As soon as a GitHub release exists, version, size, date
// and both download links (installer + portable ZIP) are taken from the latest release automatically.
const RELEASE = {
  repo: "calvinistkrass/GameBoost",
  version: "1.2.0",
  setup: "GameBoost-Setup-1.2.0.exe",
  zip: "GameBoost-1.2.0-win-x64.zip",
  size: "approx. 60 MB",
};

const base = `https://github.com/${RELEASE.repo}/releases`;

function apply({ version, setupUrl, zipUrl, size, date }) {
  document.querySelectorAll("[data-version]").forEach((el) => (el.textContent = version));
  document.querySelectorAll("[data-size]").forEach((el) => (el.textContent = size));
  document.querySelectorAll("[data-download-link]").forEach((el) => (el.href = setupUrl));
  document.querySelectorAll("[data-portable-link]").forEach((el) => (el.href = zipUrl));
  document.querySelectorAll("[data-date]").forEach((el) => (el.textContent = date ? ` · ${date}` : ""));
}

apply({
  version: RELEASE.version,
  size: RELEASE.size,
  setupUrl: `${base}/download/v${RELEASE.version}/${RELEASE.setup}`,
  zipUrl: `${base}/download/v${RELEASE.version}/${RELEASE.zip}`,
});
document.getElementById("year").textContent = new Date().getFullYear();

// Latest release from GitHub (public API, no login)
fetch(`https://api.github.com/repos/${RELEASE.repo}/releases/latest`, { headers: { Accept: "application/vnd.github+json" } })
  .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
  .then((rel) => {
    const assets = rel.assets || [];
    const setup = assets.find((a) => /setup.*\.exe$/i.test(a.name));
    const zip = assets.find((a) => /win-x64\.zip$/i.test(a.name)) || assets.find((a) => /\.zip$/i.test(a.name));
    if (!setup && !zip) return;
    const main = setup || zip;
    apply({
      version: String(rel.tag_name || RELEASE.version).replace(/^v/i, ""),
      size: `${(main.size / 1024 / 1024).toFixed(0)} MB`,
      setupUrl: main.browser_download_url,
      zipUrl: (zip || main).browser_download_url,
      date: rel.published_at ? new Date(rel.published_at).toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" }) : "",
    });
  })
  .catch(() => { /* keep the fallback values */ });
