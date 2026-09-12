const fs = require('fs');
const path = require('path');

const specFilePath = path.join(__dirname, '..', 'docs', 'spec', 'en', '09_comprehensive_example.md');
const sourceFile = path.join(__dirname, '..', 'tests', 'Kassyi.GSharp.Tests.Shared', 'TestCases', 'GEN-06_ComprehensiveSpecKernel.cu.cs');
function findSnapshotFile(filenamePattern) {
    const candidateDirs = [
        path.join(__dirname, '..', 'tests', 'Kassyi.GSharp.Tests.Cpu', 'Snapshots'),
        path.join(__dirname, '..', 'tests', 'Kassyi.GSharp.Tests.Cuda', 'Snapshots'),
        path.join(__dirname, '..', 'tests', 'Kassyi.GSharp.Tests.Shared', 'Generator', 'Snapshots')
    ];

    for (const dir of candidateDirs) {
        if (fs.existsSync(dir)) {
            const files = fs.readdirSync(dir);
            const found = files.find(f => f.includes(filenamePattern));
            if (found) {
                return path.join(dir, found);
            }
        }
    }
    return null;
}

const generatedFile = findSnapshotFile('ComprehensiveSpecKernel_ProcessPhysics_Wrapper.g.verified.cs');

console.log("Syncing comprehensive spec...");

if (!fs.existsSync(sourceFile)) {
    console.error(`Source file not found: ${sourceFile}`);
    process.exit(1);
}

if (!generatedFile || !fs.existsSync(generatedFile)) {
    console.error(`Generated file matching 'ComprehensiveSpecKernel_ProcessPhysics_Wrapper.g.verified.cs' not found in test snapshot directories.`);
    process.exit(1);
}

const sourceCode = fs.readFileSync(sourceFile, 'utf8').trimEnd();
const generatedCode = fs.readFileSync(generatedFile, 'utf8').trimEnd();
let specContent = fs.readFileSync(specFilePath, 'utf8');

function updateSectionRaw(content, beginMarker, endMarker, language, newCode) {
    const beginIdx = content.indexOf(beginMarker);
    const endIdx = content.indexOf(endMarker);

    if (beginIdx >= 0 && endIdx > beginIdx) {
        const prefix = content.substring(0, beginIdx + beginMarker.length);
        const suffix = content.substring(endIdx);
        const nl = '\n';
        return prefix + nl + '```' + language + nl + newCode + nl + '```' + nl + suffix;
    }
    return content;
}

specContent = updateSectionRaw(specContent, "<!-- BEGIN CSHARP -->", "<!-- END CSHARP -->", "csharp", sourceCode);
specContent = updateSectionRaw(specContent, "<!-- BEGIN CPP -->", "<!-- END CPP -->", "cpp", generatedCode);

fs.writeFileSync(specFilePath, specContent, 'utf8');

console.log("Sync complete!");
