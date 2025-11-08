const fs = require("fs");

const VERSION = process.env.VERSION;

const csprojPath =
  "./Jellyfin.Plugin.AutoCollections/Jellyfin.Plugin.AutoCollections.csproj";
if (!fs.existsSync(csprojPath)) {
  console.error("Jellyfin.Plugin.AutoCollections.csproj file not found");
  process.exit(1);
}

function incrementVersion(version) {
  return VERSION;
  const parts = version.split(".").map(Number);
  parts[parts.length - 1] += 1;
  return parts.join(".");
}

fs.readFile(csprojPath, "utf8", (err, data) => {
  if (err) {
    return console.error("Failed to read .csproj file:", err);
  }

  let newAssemblyVersion = null;
  let newFileVersion = null;

  const updatedData = data
    .replace(/<AssemblyVersion>(.*?)<\/AssemblyVersion>/, (match, version) => {
      newAssemblyVersion = incrementVersion(version);
      return `<AssemblyVersion>${newAssemblyVersion}</AssemblyVersion>`;
    })
    .replace(/<FileVersion>(.*?)<\/FileVersion>/, (match, version) => {
      newFileVersion = incrementVersion(version);
      return `<FileVersion>${newFileVersion}</FileVersion>`;
    });

  fs.writeFile(csprojPath, updatedData, "utf8", (err) => {
    if (err) {
      return console.error("Failed to write .csproj file:", err);
    }
    console.log("Version incremented successfully!");
  });
});
