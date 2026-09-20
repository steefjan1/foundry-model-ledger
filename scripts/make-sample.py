"""Builds samples/sample-catalog.json: a realistic, fictional-but-shaped-like-the-real-thing snapshot of
the ARM model catalog, Retail Prices meters, regions and deployments for swedencentral.

Real data comes from scripts/export-snapshot.ps1. This generator exists so the UI and the matcher can be
exercised without a subscription. Prices and dates are illustrative.
"""
import json, datetime as dt, pathlib

TODAY = dt.date(2026, 9, 20)
def iso(d): return d.strftime("%Y-%m-%dT00:00:00Z")
def days(n): return iso(TODAY + dt.timedelta(days=n))

def sku(name, mn=1, mx=1000, step=1, default=1, dep=None):
    s = {"name": name, "usageName": f"OpenAI.{name}.{{}}", "capacity": {"minimum": mn, "maximum": mx, "step": step, "default": default}}
    if dep: s["deprecationDate"] = dep
    return s

CHAT = {"chatCompletion": "true", "responses": "true", "jsonObjectResponse": "true", "jsonSchemaResponse": "true", "assistants": "true", "maxContextToken": "400000", "maxOutputToken": "128000"}
CHAT_TOOLS = dict(CHAT, **{"toolCalling": "true", "imageInput": "true"})
EMB = {"embeddings": "true", "inference": "true", "maxContextToken": "8191"}

def model(name, version, status, created, dep_inf=None, caps=CHAT_TOOLS, fmt="OpenAI", kind="OpenAI", default=False, skus=None, extra=None, publisher=None):
    m = {"format": fmt, "name": name, "version": version, "lifecycleStatus": status, "isDefaultVersion": default,
         "capabilities": dict(caps), "systemData": {"createdBy": "Microsoft", "createdAt": created, "lastModifiedAt": created},
         "skus": skus or [sku("Standard", 1, 1000, 1, 10), sku("GlobalStandard", 1, 5000, 1, 50), sku("DataZoneStandard", 1, 2000, 1, 50), sku("ProvisionedManaged", 15, 1000, 5, 100), sku("GlobalProvisionedManaged", 15, 1000, 5, 100), sku("GlobalBatch", 1, 5000, 1, 100)]}
    if publisher: m["publisher"] = publisher
    if dep_inf: m["deprecation"] = {"inference": dep_inf}
    if extra: m["capabilities"].update(extra)
    return {"kind": kind, "skuName": "S0", "model": m}

catalog = [
    # GPT-5.6 family (newest)
    model("gpt-5.6-sol", "2026-07-09", "GenerallyAvailable", "2026-07-09T00:00:00Z", days(548), default=True, extra={"maxContextToken": "1000000"}),
    model("gpt-5.6-luna", "2026-07-09", "GenerallyAvailable", "2026-07-09T00:00:00Z", days(548), default=True),
    model("gpt-5.6-terra", "2026-07-09", "GenerallyAvailable", "2026-07-09T00:00:00Z", days(548), default=True),
    model("gpt-chat-latest", "2026-08-06", "GenerallyAvailable", "2026-08-06T00:00:00Z", None, default=True),
    model("gpt-5.5", "2026-04-24", "GenerallyAvailable", "2026-04-24T00:00:00Z", days(420), default=True),
    model("gpt-5.4", "2026-03-05", "GenerallyAvailable", "2026-03-05T00:00:00Z", days(300), default=True),
    model("gpt-5.4-pro", "2026-03-05", "GenerallyAvailable", "2026-03-05T00:00:00Z", days(300), default=True,
          skus=[sku("GlobalStandard", 1, 1000, 1, 10), sku("DataZoneStandard", 1, 1000, 1, 10), sku("GlobalBatch", 1, 1000, 1, 10)]),
    model("gpt-5.4-mini", "2026-03-17", "GenerallyAvailable", "2026-03-17T00:00:00Z", days(300), default=True),
    model("gpt-5.4-nano", "2026-03-17", "GenerallyAvailable", "2026-03-17T00:00:00Z", days(300), default=True),
    model("gpt-5.3-chat", "2026-03-03", "GenerallyAvailable", "2026-03-03T00:00:00Z", days(180), default=True),
    model("gpt-5.3-codex", "2026-02-24", "GenerallyAvailable", "2026-02-24T00:00:00Z", days(180), default=True, caps=dict(CHAT, toolCalling="true")),
    model("gpt-5.2-chat", "2026-02-10", "GenerallyAvailable", "2026-02-10T00:00:00Z", days(75), default=True),
    model("gpt-5.2", "2025-12-11", "Deprecating", "2025-12-11T00:00:00Z", days(60), default=True),
    model("gpt-5.1", "2025-11-13", "Deprecating", "2025-11-13T00:00:00Z", days(40), default=True),
    model("gpt-5.1-codex-mini", "2025-11-13", "Deprecating", "2025-11-13T00:00:00Z", days(40), default=True, caps=dict(CHAT, toolCalling="true")),
    model("gpt-5", "2025-08-07", "Deprecating", "2025-08-07T00:00:00Z", days(25), default=True),
    model("gpt-5-pro", "2025-10-06", "Deprecating", "2025-10-06T00:00:00Z", days(25), default=True, skus=[sku("GlobalStandard", 1, 1000, 1, 10), sku("DataZoneStandard", 1, 1000, 1, 10)]),
    model("gpt-5-mini", "2025-08-07", "Deprecating", "2025-08-07T00:00:00Z", days(25), default=True),
    model("gpt-5-nano", "2025-08-07", "Deprecating", "2025-08-07T00:00:00Z", days(25), default=True),
    model("gpt-5-chat", "2025-08-07", "Deprecating", "2025-08-07T00:00:00Z", days(25), default=True),
    # GPT-4.1 (legacy)
    model("gpt-4.1", "2025-04-14", "Deprecated", "2025-04-14T00:00:00Z", days(-20), default=True, extra={"maxContextToken": "1047576"}),
    model("gpt-4.1-mini", "2025-04-14", "Deprecated", "2025-04-14T00:00:00Z", days(-20), default=True),
    model("gpt-4.1-nano", "2025-04-14", "Deprecated", "2025-04-14T00:00:00Z", days(-20), default=True),
    # GPT-4o (two versions, one still listed although closed to new deployments)
    model("gpt-4o", "2024-11-20", "Deprecated", "2024-11-20T00:00:00Z", days(-120), default=True, extra={"maxContextToken": "128000"}),
    model("gpt-4o", "2024-08-06", "Deprecated", "2024-08-06T00:00:00Z", days(-120)),
    model("gpt-4o-mini", "2024-07-18", "Deprecated", "2024-07-18T00:00:00Z", days(-150), default=True),
    # o-series
    model("o3", "2025-04-16", "Deprecating", "2025-04-16T00:00:00Z", days(100), default=True, caps=dict(CHAT, toolCalling="true", reasoning="true")),
    model("o3-mini", "2025-01-31", "Deprecating", "2025-01-31T00:00:00Z", days(50), default=True, caps=dict(CHAT, reasoning="true")),
    model("o4-mini", "2025-04-16", "Deprecating", "2025-04-16T00:00:00Z", days(100), default=True, caps=dict(CHAT, toolCalling="true", reasoning="true")),
    model("o1", "2024-12-17", "Deprecated", "2024-12-17T00:00:00Z", days(-60), default=True, caps=dict(CHAT, reasoning="true")),
    # router, embeddings, media, speech
    model("model-router", "2025-11-18", "GenerallyAvailable", "2025-11-18T00:00:00Z", days(400), default=True, caps=dict(CHAT), skus=[sku("GlobalStandard", 1, 5000, 1, 50), sku("DataZoneStandard", 1, 2000, 1, 50)]),
    model("text-embedding-3-small", "1", "GenerallyAvailable", "2024-01-25T00:00:00Z", days(500), default=True, caps=EMB, skus=[sku("Standard", 1, 350, 1, 120), sku("GlobalStandard", 1, 5000, 1, 350)]),
    model("text-embedding-3-large", "1", "GenerallyAvailable", "2024-01-25T00:00:00Z", days(500), default=True, caps=EMB, skus=[sku("Standard", 1, 350, 1, 120), sku("GlobalStandard", 1, 5000, 1, 350)]),
    model("text-embedding-ada-002", "2", "Deprecating", "2022-12-06T00:00:00Z", days(35), default=True, caps=EMB, skus=[sku("Standard", 1, 350, 1, 120)]),
    model("gpt-image-1.5", "2025-12-16", "GenerallyAvailable", "2025-12-16T00:00:00Z", days(400), default=True, caps={"imageGenerations": "true", "inference": "true"}, skus=[sku("GlobalStandard", 1, 100, 1, 2)]),
    model("gpt-image-2", "2026-04-21", "Preview", "2026-04-21T00:00:00Z", None, default=True, caps={"imageGenerations": "true", "inference": "true"}, skus=[sku("GlobalStandard", 1, 100, 1, 2)]),
    model("gpt-realtime-2.1", "2026-07-07", "GenerallyAvailable", "2026-07-07T00:00:00Z", days(500), default=True, caps={"realtime": "true", "audio": "true", "inference": "true"}, skus=[sku("GlobalStandard", 1, 100, 1, 1), sku("DataZoneStandard", 1, 100, 1, 1)]),
    model("whisper", "001", "GenerallyAvailable", "2023-09-15T00:00:00Z", days(200), default=True, caps={"audio": "true", "inference": "true"}, skus=[sku("Standard", 1, 3, 1, 1)]),
    model("sora-2", "2025-10-06", "Preview", "2025-10-06T00:00:00Z", None, default=True, caps={"videoGenerations": "true", "inference": "true"}, skus=[sku("GlobalStandard", 1, 10, 1, 1)]),
    # Other publishers
    model("claude-sonnet-5", "1", "GenerallyAvailable", "2026-05-12T00:00:00Z", None, fmt="Anthropic", kind="AIServices", default=True, publisher="Anthropic", skus=[sku("GlobalStandard", 1, 1000, 1, 1)]),
    model("claude-opus-5", "1", "GenerallyAvailable", "2026-05-12T00:00:00Z", None, fmt="Anthropic", kind="AIServices", default=True, publisher="Anthropic", skus=[sku("GlobalStandard", 1, 1000, 1, 1)]),
    model("grok-4.6", "1", "GenerallyAvailable", "2026-06-02T00:00:00Z", None, fmt="xAI", kind="AIServices", default=True, publisher="xAI", skus=[sku("GlobalStandard", 1, 1000, 1, 1), sku("DataZoneStandard", 1, 1000, 1, 1)]),
    model("grok-3", "1", "Deprecating", "2025-05-19T00:00:00Z", days(45), fmt="xAI", kind="AIServices", default=True, publisher="xAI", skus=[sku("GlobalStandard", 1, 1000, 1, 1)]),
    model("DeepSeek-V4", "1", "GenerallyAvailable", "2026-03-30T00:00:00Z", None, fmt="DeepSeek", kind="AIServices", default=True, publisher="DeepSeek", skus=[sku("GlobalStandard", 1, 1000, 1, 1)]),
    model("DeepSeek-R1", "1", "Deprecating", "2025-01-29T00:00:00Z", days(80), fmt="DeepSeek", kind="AIServices", default=True, publisher="DeepSeek", skus=[sku("GlobalStandard", 1, 1000, 1, 1)]),
    model("Mistral-Large-3", "1", "GenerallyAvailable", "2026-01-15T00:00:00Z", None, fmt="Mistral AI", kind="AIServices", default=True, publisher="Mistral AI", skus=[sku("GlobalStandard", 1, 1000, 1, 1)]),
    model("Llama-4-Maverick-17B-128E-Instruct-FP8", "1", "GenerallyAvailable", "2025-04-05T00:00:00Z", None, fmt="Meta", kind="AIServices", default=True, publisher="Meta", skus=[sku("GlobalStandard", 1, 1000, 1, 1)]),
    model("Phi-4", "7", "GenerallyAvailable", "2024-12-12T00:00:00Z", None, fmt="Microsoft", kind="AIServices", default=True, publisher="Microsoft", skus=[sku("GlobalStandard", 1, 1000, 1, 1)]),
]

# ----- Retail Prices meters, in the real naming styles -----
meters = []
def meter(product, sku_name, price, unit="1M", meter_name=None, typ="Consumption"):
    meters.append({"currencyCode": "USD", "retailPrice": price, "unitPrice": price, "armRegionName": "swedencentral", "location": "SE Central",
                   "meterId": f"m{len(meters):04d}", "meterName": meter_name or f"{sku_name} {'1M ' if unit == '1M' else ''}Tokens",
                   "skuName": sku_name, "productName": product, "serviceName": "Foundry Models", "serviceFamily": "AI + Machine Learning",
                   "unitOfMeasure": unit, "type": typ, "effectiveStartDate": "2026-01-01T00:00:00Z"})

G5 = "Azure OpenAI GPT5"
for v, inp, out, cd in [("5.6 sol", 5.0, 20.0, 0.5), ("5.6 luna", 0.4, 1.6, 0.04), ("5.6 terra", 2.0, 12.0, 0.2)]:
    for dz, mul in [("Gl", 1.0), ("DZ", 1.1)]:
        meter(G5, f"{v} ShortCo Inp Std {dz}", round(inp*mul, 4)); meter(G5, f"{v} ShortCo Opt Std {dz}", round(out*mul, 4)); meter(G5, f"{v} ShortCo Cd Inp Std {dz}", round(cd*mul, 4))
        meter(G5, f"{v} LongCo Inp Std {dz}", round(inp*2*mul, 4)); meter(G5, f"{v} LongCo Opt Std {dz}", round(out*1.5*mul, 4))
        meter(G5, f"{v} ShortCo Inp PP {dz}", round(inp*2*mul, 4)); meter(G5, f"{v} ShortCo Opt PP {dz}", round(out*2*mul, 4))
        meter(G5, f"{v} ShortCo Batch Inp {dz}", round(inp*0.5*mul, 4)); meter(G5, f"{v} ShortCo Batch Opt {dz}", round(out*0.5*mul, 4))
meter(G5, "chat-latest 08062026 inp Gl", 5.0); meter(G5, "chat-latest 08062026 opt Gl", 20.0); meter(G5, "chat-latest 08062026 inp Dz", 5.5); meter(G5, "chat-latest 08062026 opt Dz", 22.0)
meter(G5, "5.5 ShortCo Inp Std Gl", 3.0); meter(G5, "5.5 ShortCo Opt Std Gl", 15.0); meter(G5, "5.5 ShortCo Cd Inp Std Gl", 0.3); meter(G5, "5.5 ShortCo Batch cd inp Gl", 0.25); meter(G5, "5.5 ShortCo PP opt Dz", 82.5)
meter(G5, "5.4 inp Gl", 2.5); meter(G5, "5.4 opt Gl", 10.0); meter(G5, "5.4 cd inp Gl", 0.25); meter(G5, "5.4 inp Dz", 2.75); meter(G5, "5.4 opt Dz", 11.0)
meter(G5, "5.4 pro inp Gl", 30.0); meter(G5, "5.4 pro opt Gl", 180.0); meter(G5, "5.4 pro Batch opt Dz", 99.0); meter(G5, "5.4 pro longco opt Dz", 297.0); meter(G5, "5.4 pro longco batch inp Dz", 33.0)
meter(G5, "5.4 mini Inp Gl", 0.75); meter(G5, "5.4 mini Opt Gl", 3.0); meter(G5, "5.4 mini Batch Inp Gl", 0.375); meter(G5, "5.4 mini cd inp Gl", 0.075)
meter(G5, "5.4 nano Inp Gl", 0.2); meter(G5, "5.4 nano Opt Gl", 0.8)
meter(G5, "5.3 chat inp Gl", 1.75); meter(G5, "5.3 chat opt Gl", 14.0)
meter(G5, "5.3 codex inp Gl", 1.75); meter(G5, "5.3 codex opt Gl", 14.0); meter(G5, "5.3 codex pp inp Gl", 3.5); meter(G5, "5.3 codex pp cd inp Dz", 0.385)
meter(G5, "5.2 chat 0210 inp Gl", 1.75); meter(G5, "5.2 chat 0210 opt Gl", 14.0)
meter(G5, "5.2 1211 inp Gl", 1.75); meter(G5, "5.2 1211 opt Gl", 14.0); meter(G5, "5.2 1211 cd inp Gl", 0.175)
meter(G5, "5.1 inp Gl", 1.25); meter(G5, "5.1 opt Gl", 10.0); meter(G5, "5.1 pp opt Dz", 22.0)
meter(G5, "5.1 codex mini inp Dz", 0.275); meter(G5, "5.1 codex mini opt Dz", 2.2)
meter(G5, "GPT 5 inpt Glbl", 1.25); meter(G5, "GPT 5 outpt Glbl", 10.0); meter(G5, "5 pp cd inp Gl", 0.25)
meter(G5, "gpt 5 pro inp dzone", 0.0165, "1K", "gpt 5 pro inp dzone Tokens"); meter(G5, "gpt 5 pro out glbl", 0.12, "1K", "gpt 5 pro out glbl Tokens")
meter(G5, "gpt 5 mini inp glbl", 0.00025, "1K", "gpt 5 mini inp glbl Tokens"); meter(G5, "gpt 5 mini outp glbl", 0.002, "1K", "gpt 5 mini outp glbl Tokens")
meter(G5, "gpt 5 nano inp glbl", 0.00005, "1K", "gpt 5 nano inp glbl Tokens"); meter(G5, "gpt 5 nano outp glbl", 0.0004, "1K", "gpt 5 nano outp glbl Tokens")
meter(G5, "gpt 5 chat inp glbl", 0.00125, "1K", "gpt 5 chat inp glbl Tokens"); meter(G5, "gpt 5 chat outp glbl", 0.01, "1K", "gpt 5 chat outp glbl Tokens")

OA = "Azure OpenAI"
for n, inp, out, cd in [("gpt 4.1", 0.002, 0.008, 0.0005), ("gpt 4.1 mini", 0.0004, 0.0016, 0.0001), ("gpt 4.1 nano", 0.0001, 0.0004, 0.000025)]:
    meter(OA, f"{n} Inp glbl", inp, "1K", f"{n} Inp glbl Tokens"); meter(OA, f"{n} Outp glbl", out, "1K", f"{n} Outp glbl Tokens"); meter(OA, f"{n} cached Inp glbl", cd, "1K", f"{n} cached Inp glbl Tokens")
    meter(OA, f"{n} Inp regnl", round(inp*1.21, 6), "1K", f"{n} Inp regnl Tokens"); meter(OA, f"{n} Outp regnl", round(out*1.21, 6), "1K", f"{n} Outp regnl Tokens")
meter(OA, "gpt-4o-1120-Inp-glbl", 0.0025, "1K", "gpt-4o-1120-Inp-glbl Tokens"); meter(OA, "gpt-4o-1120-Outp-glbl", 0.01, "1K", "gpt-4o-1120-Outp-glbl Tokens")
meter(OA, "gpt-4o-0806-Inp-glbl", 0.0025, "1K", "gpt-4o-0806-Inp-glbl Tokens"); meter(OA, "gpt-4o-0806-Outp-glbl", 0.01, "1K", "gpt-4o-0806-Outp-glbl Tokens")
meter(OA, "gpt-4o-mini-0718-Inp-glbl", 0.00015, "1K", "gpt-4o-mini-0718-Inp-glbl Tokens"); meter(OA, "gpt-4o-mini-0718-Outp-regnl", 0.00066, "1K", "gpt-4o-mini-0718-Outp-regnl Tokens")
R = "Azure OpenAI Reasoning"
meter(R, "o3 0416 Inp glbl", 0.002, "1K", "o3 0416 Inp glbl Tokens"); meter(R, "o3 0416 Outp glbl", 0.008, "1K", "o3 0416 Outp glbl Tokens"); meter(R, "o3 0416 Batch Outp glbl", 0.004, "1K", "o3 0416 Batch Outp glbl Tokens")
meter(R, "o3 mini 0131 Inp glbl", 0.0011, "1K", "o3 mini 0131 Inp glbl Tokens"); meter(R, "o3 mini 0131 Outp glbl", 0.0044, "1K", "o3 mini 0131 Outp glbl Tokens"); meter(R, "o3 mini 0131 Batch Outp Data Zone", 0.00242, "1K", "o3 mini 0131 Batch Outp Data Zone Tokens")
meter(R, "o4 mini 0416 Inp glbl", 0.0011, "1K", "o4 mini 0416 Inp glbl Tokens"); meter(R, "o4 mini 0416 Outp glbl", 0.0044, "1K", "o4 mini 0416 Outp glbl Tokens")
meter(R, "o1 1217 Inp glbl", 0.015, "1K", "o1 1217 Inp glbl Tokens"); meter(R, "o1 1217 Outp glbl", 0.06, "1K", "o1 1217 Outp glbl Tokens"); meter(R, "o1 1217 cached Inp glbl", 0.0075, "1K", "o1 1217 cached Inp glbl Tokens"); meter(R, "o1 1217 Inp regnl", 0.01815, "1K", "o1 1217 Inp regnl Tokens")
meter(OA, "text-embedding-3-small-global", 0.00002, "1K", "text-embedding-3-small-global Tokens"); meter(OA, "text-embedding-3-small-regional", 0.000025, "1K", "text-embedding-3-small-regional Tokens")
meter(OA, "text-embedding-3-large-global", 0.00013, "1K", "text-embedding-3-large-global Tokens")
meter(OA, "text-embedding-ada-002 regional", 0.0001, "1K", "text-embedding-ada-002 regional Tokens")
meter(OA, "model router inp glbl", 0.0, "1K", "model router inp glbl Tokens")
M = "Azure OpenAI Media"
meter(M, "gpt img 1.5 in txt gl", 5.0); meter(M, "gpt img 1.5 in img gl", 8.0); meter(M, "gpt img 1.5 opt img gl", 32.0)
meter(M, "gpt img 2 in txt gl", 5.0); meter(M, "gpt img 2 opt img gl", 40.0)
meter(M, "gpt-realtime-2.1 Text inp Gl", 4.0); meter(M, "gpt-realtime-2.1 Text opt Gl", 16.0); meter(M, "gpt-realtime-2.1 Audio inp Gl", 32.0); meter(M, "gpt-realtime-2.1 Audio opt Gl", 64.0); meter(M, "gpt-realtime-2.1 Audio cd inp Gl", 0.4); meter(M, "gpt-realtime-2.1 Text opt DZ", 26.4)
meter(M, "Whisper", 0.36, "1 Hour", "Whisper Hours"); meter(M, "Sora 720p 1-5s glbl", 0.45, "1 Second", "Sora 720p 1-5s glbl Video"); meter(M, "Sora 1080p Sq 16-20s glbl", 1.35, "1 Second", "Sora 1080p Sq 16-20s glbl Video")
meter("Azure Anthropic Models", "Sonnet 5 Inp glbl", 3.0); meter("Azure Anthropic Models", "Sonnet 5 Outp glbl", 15.0); meter("Azure Anthropic Models", "Sonnet 5 Cd Inp glbl", 0.3)
meter("Azure Anthropic Models", "Opus 5 Inp glbl", 15.0); meter("Azure Anthropic Models", "Opus 5 Outp glbl", 75.0)
meter("Azure Grok Models", "4.6 Inp Glbl", 0.002, "1K", "4.6 Inp Glbl Tokens"); meter("Azure Grok Models", "4.6 Outp Glbl L", 0.012, "1K", "4.6 Outp Glbl L Tokens"); meter("Azure Grok Models", "4.6 Inp DZ", 0.0024, "1K", "4.6 Inp DZ Tokens")
meter("Azure Grok Models", "Grok-3 Inp glbl", 0.003, "1K", "Grok-3 Inp glbl Tokens"); meter("Azure Grok Models", "Grok-3 Outp glbl", 0.015, "1K", "Grok-3 Outp glbl Tokens"); meter("Azure Grok Models", "Provisioned Managed Data Zone", 1.2, "1/Hour", "Provisioned Managed Data Zone Unit")
meter("Azure Deepseek Models", "V4 Inp glbl", 0.00027, "1K", "V4 Inp glbl Tokens"); meter("Azure Deepseek Models", "V4 Outp glbl", 0.0011, "1K", "V4 Outp glbl Tokens")
meter("Azure Deepseek Models", "R1 Inp glbl", 0.00135, "1K", "R1 Inp glbl Tokens"); meter("Azure Deepseek Models", "R1 Outp glbl", 0.0054, "1K", "R1 Outp glbl Tokens")
meter("Azure Mistral Models", "Large 3 Inp glbl", 0.002, "1K", "Large 3 Inp glbl Tokens"); meter("Azure Mistral Models", "Large 3 Outp glbl", 0.006, "1K", "Large 3 Outp glbl Tokens"); meter("Azure Mistral Models", "MM3.5 Outp DZ", 0.00825, "1K", "MM3.5 Outp DZ Tokens")
meter("Azure Llama Models", "Llama 4 Maverick 17B Inp regnl", 0.000303, "1K", "Llama 4 Maverick 17B Inp regnl Tokens"); meter("Azure Llama Models", "Llama 4 Maverick 17B Outp regnl", 0.00097, "1K", "Llama 4 Maverick 17B Outp regnl Tokens")
meter("Azure Phi Models", "Phi 4 Inp glbl", 0.000125, "1K", "Phi 4 Inp glbl Tokens"); meter("Azure Phi Models", "Phi 4 Outp glbl", 0.0005, "1K", "Phi 4 Outp glbl Tokens")
meter("Cohere Models", "Command A Plus Outp DZ", 3.52, "1M", "Command A Plus Outp DZ 1M Tokens")

# Real Azure region coordinates (approximate) so the availability map has something to show offline.
_R = [
 ("westeurope","West Europe","Europe",52.37,4.89,True),("northeurope","North Europe","Europe",53.35,-6.26,True),("swedencentral","Sweden Central","Europe",60.67,17.14,True),
 ("francecentral","France Central","Europe",46.36,2.37,True),("francesouth","France South","Europe",43.83,5.43,False),("germanywestcentral","Germany West Central","Europe",50.11,8.68,True),
 ("germanynorth","Germany North","Europe",53.07,8.81,False),("norwayeast","Norway East","Europe",59.91,10.75,True),("norwaywest","Norway West","Europe",58.97,5.73,False),
 ("switzerlandnorth","Switzerland North","Europe",47.45,8.56,True),("switzerlandwest","Switzerland West","Europe",46.20,6.14,False),("uksouth","UK South","Europe",50.94,-0.80,True),
 ("ukwest","UK West","Europe",53.43,-3.08,False),("polandcentral","Poland Central","Europe",52.23,21.01,True),("italynorth","Italy North","Europe",45.46,9.19,True),("spaincentral","Spain Central","Europe",40.42,-3.70,True),
 ("eastus","East US","US",37.37,-79.82,True),("eastus2","East US 2","US",36.68,-78.39,True),("centralus","Central US","US",41.59,-93.62,True),("westus","West US","US",37.78,-122.42,True),
 ("westus2","West US 2","US",47.23,-119.85,False),("westus3","West US 3","US",33.45,-112.07,True),("southcentralus","South Central US","US",29.42,-98.49,True),("northcentralus","North Central US","US",41.88,-87.63,True),
 ("canadacentral","Canada Central","Canada",43.65,-79.38,True),("canadaeast","Canada East","Canada",46.82,-71.21,True),("brazilsouth","Brazil South","South America",-23.55,-46.63,True),("chilecentral","Chile Central","South America",-33.45,-70.67,False),
 ("mexicocentral","Mexico Central","Mexico",20.59,-100.39,False),("japaneast","Japan East","Asia Pacific",35.68,139.77,True),("koreacentral","Korea Central","Asia Pacific",37.57,126.98,True),("eastasia","East Asia","Asia Pacific",22.27,114.19,True),
 ("southeastasia","Southeast Asia","Asia Pacific",1.28,103.85,True),("australiaeast","Australia East","Asia Pacific",-33.87,151.21,True),("centralindia","Central India","Asia Pacific",18.59,73.92,True),("southindia","South India","Asia Pacific",12.98,80.16,True),
 ("southafricanorth","South Africa North","Africa",-25.73,28.22,True),("uaenorth","UAE North","Middle East",25.27,55.30,True),("israelcentral","Israel Central","Middle East",31.20,34.85,False),("qatarcentral","Qatar Central","Middle East",25.55,51.44,True),
]
regions = [{"name": n, "displayName": d, "geography": g, "latitude": la, "longitude": lo, "hostsFoundry": h} for n, d, g, la, lo, h in _R]

deployments = [
    {"id": "/subscriptions/0000/resourceGroups/rg-rag-pipeline/providers/Microsoft.CognitiveServices/accounts/aoai-rag-swc/deployments/chat", "name": "chat", "accountName": "aoai-rag-swc", "resourceGroup": "rg-rag-pipeline", "region": "swedencentral", "modelName": "gpt-5.4-mini", "modelVersion": "2026-03-17", "modelFormat": "OpenAI", "skuName": "GlobalStandard", "capacity": 50, "provisioningState": "Succeeded", "versionUpgradeOption": "OnceNewDefaultVersionAvailable"},
    {"id": "/subscriptions/0000/resourceGroups/rg-rag-pipeline/providers/Microsoft.CognitiveServices/accounts/aoai-rag-swc/deployments/embed", "name": "embed", "accountName": "aoai-rag-swc", "resourceGroup": "rg-rag-pipeline", "region": "swedencentral", "modelName": "text-embedding-3-small", "modelVersion": "1", "modelFormat": "OpenAI", "skuName": "GlobalStandard", "capacity": 120, "provisioningState": "Succeeded", "versionUpgradeOption": "NoAutoUpgrade"},
    {"id": "/subscriptions/0000/resourceGroups/rg-five-rag/providers/Microsoft.CognitiveServices/accounts/aoai-fiverag/deployments/gpt-4.1", "name": "gpt-4.1", "accountName": "aoai-fiverag", "resourceGroup": "rg-five-rag", "region": "swedencentral", "modelName": "gpt-4.1", "modelVersion": "2025-04-14", "modelFormat": "OpenAI", "skuName": "GlobalStandard", "capacity": 30, "provisioningState": "Succeeded", "versionUpgradeOption": "OnceCurrentVersionExpired"},
    {"id": "/subscriptions/0000/resourceGroups/rg-agents/providers/Microsoft.CognitiveServices/accounts/aoai-agents-cus/deployments/gpt-5", "name": "gpt-5", "accountName": "aoai-agents-cus", "resourceGroup": "rg-agents", "region": "swedencentral", "modelName": "gpt-5", "modelVersion": "2025-08-07", "modelFormat": "OpenAI", "skuName": "GlobalStandard", "capacity": 20, "provisioningState": "Succeeded", "versionUpgradeOption": "OnceNewDefaultVersionAvailable"},
    {"id": "/subscriptions/0000/resourceGroups/rg-agents/providers/Microsoft.CognitiveServices/accounts/aoai-agents-cus/deployments/o3-mini", "name": "o3-mini", "accountName": "aoai-agents-cus", "resourceGroup": "rg-agents", "region": "swedencentral", "modelName": "o3-mini", "modelVersion": "2025-01-31", "modelFormat": "OpenAI", "skuName": "DataZoneStandard", "capacity": 10, "provisioningState": "Succeeded", "versionUpgradeOption": "NoAutoUpgrade"},
]

out = {"_note": "Illustrative snapshot generated by scripts/make-sample.py. Prices and dates are NOT real. Export a real one with scripts/export-snapshot.ps1.",
       "region": "swedencentral", "catalog": catalog, "meters": meters, "regions": regions, "deployments": deployments}
path = pathlib.Path(__file__).resolve().parent.parent / "samples" / "sample-catalog.json"
path.write_text(json.dumps(out, indent=1))
print(f"wrote {path}: {len(catalog)} models, {len(meters)} meters")
