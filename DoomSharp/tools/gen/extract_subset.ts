// Extracts a compact characterization subset from the full C dump
// (DoomSharp/tools/charact/vectors.json) for embedding in the test project.
//
// Run from repo root:  node DoomSharp/tools/gen/extract_subset.ts
import { readFileSync, writeFileSync } from "node:fs";

const root = new URL("../../../", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1");
const vectors = JSON.parse(readFileSync(root + "DoomSharp/tools/charact/vectors.json", "utf8"));

// Deterministic subsample: every 97th vector (prime stride) keeps coverage
// across the whole fuzz range while keeping the file small.
const stride = 97;
const subset = vectors.filter((v: any) =>
  (v.op === "mul" || v.op === "div" || v.op === "div2") && typeof v.a === "number"
).filter((_, i) => i % stride === 0);

const fatals = vectors.filter((v: any) => v.fatal === true);

const out = {
  generatedBy: "DoomSharp/tools/gen/extract_subset.ts from tools/charact/vectors.json (C harness over unmodified linuxdoom-1.10 sources)",
  ops: subset,
  fatalCases: fatals,
};

writeFileSync(root + "DoomSharp/tests/DoomSharp.Tests.Unit/CharacterizationVectors.json",
  JSON.stringify(out));
console.log(`subset: ${subset.length} ops, ${fatals.length} fatal cases`);
