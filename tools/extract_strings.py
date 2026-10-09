#!/usr/bin/env python3
"""Collects every English UI string used as a localization key (views, DataAnnotations, application errors).

Usage: python3 tools/extract_strings.py            -> prints keys missing from any translation
       python3 tools/extract_strings.py --list     -> prints all keys
"""
import glob, os, re, sys, xml.etree.ElementTree as ET

ROOT = os.path.join(os.path.dirname(__file__), "..", "src")
STR = r'"((?:[^"\\]|\\.)*)"'
patterns = [
    r'\bL\[' + STR,
    r'Display\(Name = ' + STR,
    r'ErrorMessage = ' + STR,
    r'Error\.(?:Validation|Conflict|NotFound|Forbidden)\(' + STR,
    r'new ValidationResult\(' + STR,
    r'\bFlash\(' + STR,
    r'RedirectWithResult\([^;]*?, ' + STR + r', nameof',
    r'Outcome\([^;]*?, ' + STR + r', returnUrl',
    r'\b(?:hint|empty|placeholder|label)="(?!@)([^"]+)"',
    r'\(\s*"[a-z-]+",\s*' + STR + r',\s*' + STR + r'\s*\)',
    r'=> ' + STR + r',',
    r'RequestAsync\(courseId, Aborted\), ' + STR,
]
skip_files = ("PreviewFakes.cs",)
keys = set()
for path in glob.glob(os.path.join(ROOT, "**", "*.cs*"), recursive=True):
    if "/obj/" in path or "/bin/" in path or path.endswith(skip_files) or "/Persistence/" in path:
        continue
    text = open(path, encoding="utf-8").read()
    for i, p in enumerate(patterns):
        for m in re.finditer(p, text):
            for g in m.groups():
                if i == 0 and g and not g.startswith("{"):
                    keys.add(g.replace('\\"', '"'))
                elif g and not g.startswith(("@", "~", "/", "http", "application/", "image/", "video/", "audio/", "text/")) and re.search(r"[A-Za-z]", g) and not re.fullmatch(r"[a-z_.:-]+|[A-Z][a-zA-Z]+Async|[A-Z]{2,}", g):
                    keys.add(g.replace('\\"', '"'))

# Constructs that the patterns above cannot see.
keys |= {"True", "False", "Admin", "Instructor", "Student", "This field is required.", "The value '{0}' is not valid.",
         "The value is not valid.", "Please enter a number.", "The {0} field is required.",
         "{0} must be between {2} and {1} characters long.", "{0} can be at most {1} characters long.",
         "{0} must be between {1} and {2}.", "Please enter a valid email address.", "Please enter a valid phone number.",
         "Please enter a valid web address.", "{0} and {1} do not match.", "The requested item was not found.",
         "You do not have access to this item."}
# Placeholders, date patterns and single letters are not UI text.
noise = {"A", "E", "L", "P", "B1", "CKJX2020001234", "NELT-2026-XXXX-XXXX", "d MMM yyyy", "d MMMM yyyy", "d. MMM yyyy", "yyyy年M月d日", "Index"}
keys -= noise

if "--list" in sys.argv:
    print("\n".join(sorted(keys)))
    sys.exit(0)

res = os.path.join(ROOT, "Nelt.Web", "Resources")
missing = 0
for culture in ("de", "ar", "zh"):
    file = os.path.join(res, f"SharedResource.{culture}.resx")
    have = set()
    if os.path.exists(file):
        have = {d.get("name") for d in ET.parse(file).getroot().findall("data")}
    lack = sorted(keys - have)
    missing += len(lack)
    print(f"{culture}: {len(keys) - len(lack)}/{len(keys)} translated")
    for k in lack[:400]:
        print("   ", k)
sys.exit(1 if missing else 0)
