const fs = require('fs');
const path = require('path');

const repoRoot = path.join(__dirname, '..', '..');
const specFilePath = path.join(repoRoot, 'docs', 'spec', 'en', '02_architecture', '03_comprehensive_example.md');
const sourceFile = path.join(repoRoot, 'tests', 'Compiler', 'Kassyi.GSharp.Tests.Shared', 'TestCases', 'ComprehensiveSpecKernel.cu.cs');
const snapshotDir = path.join(repoRoot, 'tests', 'Compiler', 'Kassyi.GSharp.Tests.Cuda', 'Snapshots');
const snapshotPrefix = 'Snap.TestComprehensiveSpecKern_';
// The hint name is the encoded path of the kernel class (DebugReduc.ComprehensiveSpecKernel).
const snapshotSuffix = '_Cuda#DebugReduc__ComprehensiveSpecKernel.g.verified.cs';

function findSnapshotFile() {
    if (!fs.existsSync(snapshotDir)) {
        return null;
    }

    const found = fs.readdirSync(snapshotDir).find(f => f.startsWith(snapshotPrefix) && f.endsWith(snapshotSuffix));
    return found ? path.join(snapshotDir, found) : null;
}

// Snapshots are written with a BOM and the spec uses CRLF; normalize both inputs before embedding.
function readNormalized(filePath) {
    return fs.readFileSync(filePath, 'utf8').replace(/^﻿/, '').replace(/\r\n/g, '\n').trimEnd();
}

const generatedFile = findSnapshotFile();

console.log("Syncing comprehensive spec...");

if (!fs.existsSync(sourceFile)) {
    console.error(`Source file not found: ${sourceFile}`);
    process.exit(1);
}

if (!generatedFile) {
    console.error(`Generated file matching '${snapshotPrefix}*${snapshotSuffix}' not found in ${snapshotDir}.`);
    process.exit(1);
}

const sourceCode = readNormalized(sourceFile);
const generatedCode = readNormalized(generatedFile);
const originalSpec = fs.readFileSync(specFilePath, 'utf8');
const specNewLine = originalSpec.includes('\r\n') ? '\r\n' : '\n';
let specContent = originalSpec.replace(/\r\n/g, '\n');

function updateSectionRaw(content, beginMarker, endMarker, language, newCode) {
    const beginIdx = content.indexOf(beginMarker);
    const endIdx = content.indexOf(endMarker);

    if (beginIdx < 0 || endIdx <= beginIdx) {
        console.error(`Markers ${beginMarker} / ${endMarker} not found in ${specFilePath}`);
        process.exit(1);
    }

    const prefix = content.substring(0, beginIdx + beginMarker.length);
    const suffix = content.substring(endIdx);
    return prefix + '\n\n```' + language + '\n' + newCode + '\n```\n\n' + suffix;
}

specContent = updateSectionRaw(specContent, "<!-- BEGIN CSHARP -->", "<!-- END CSHARP -->", "csharp", sourceCode);
specContent = updateSectionRaw(specContent, "<!-- BEGIN CPP -->", "<!-- END CPP -->", "csharp", generatedCode);

fs.writeFileSync(specFilePath, specContent.replace(/\n/g, specNewLine), 'utf8');

console.log("Sync complete!");
