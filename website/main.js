// GameBoost website: version and download link.
// The values below are the fallback. As soon as a GitHub release with a .zip exists,
// version, size, date and link are taken from the latest release automatically.
const RELEASE = {
  repo: "calvinistkrass/GameBoost",
  version: "1.2.0",
  file: "GameBoost-1.2.0-win-x64.zip",
  size: "approx. 60 MB",
};

const fallbackUrl = `https://github.com/${RELEASE.repo}/releases/download/v${RELEASE.version}/${RELEASE.file}`;

function apply({ version, file, size, url, date }) {
  document.querySelectorAll("[data-version]").forEach((el) => (el.textContent = version));
  document.querySelectorAll("[data-filename]").forEach((el) => (el.textContent = file));
  document.querySelectorAll("[data-size]").forEach((el) => (el.textContent = size));
  document.querySelectorAll("[data-download-link]").forEach((el) => (el.href = url));
  document.querySelectorAll("[data-date]").forEach((el) => (el.textContent = date ? ` · ${date}` : ""));
}

apply({ ...RELEASE, url: fallbackUrl });
document.getElementById("year").textContent = new Date().getFullYear();

// Latest release from GitHub (public API, no login)
fetch(`https://api.github.com/repos/${RELEASE.repo}/releases/latest`, { headers: { Accept: "application/vnd.github+json" } })
  .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
  .then((rel) => {
    const asset = (rel.assets || []).find((a) => /win-x64\.zip$/i.test(a.name)) || (rel.assets || []).find((a) => /\.zip$/i.test(a.name));
    if (!asset) return;
    apply({
      version: String(rel.tag_name || RELEASE.version).replace(/^v/i, ""),
      file: asset.name,
      size: `${(asset.size / 1024 / 1024).toFixed(0)} MB`,
      url: asset.browser_download_url,
      date: rel.published_at ? new Date(rel.published_at).toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" }) : "",
    });
  })
  .catch(() => { /* keep the fallback values */ });
