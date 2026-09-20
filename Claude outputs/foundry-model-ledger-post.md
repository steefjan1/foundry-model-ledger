# Foundry Model Ledger: Which Model, at What Price, Until When, and Which of Mine

Every model conversation with a team ends with the same four questions. Is the model available in our region? What can it do? What does it cost per million tokens? And when does Microsoft retire it? Then the platform team asks a fifth: which of our own deployments are affected? Microsoft's [Foundry Model Explorer](https://foundry-models.azurewebsites.net/explorer) answers the first question well. It does not answer the other four, and it cannot, because the answers live in your subscription and in a price list that was never meant to be read by software. So I built the Foundry Model Ledger to read them together, and the interesting part is what the data looks like once you do.

[SCREENSHOT: Models tab, swedencentral, prices in EUR, gpt-5.6 filter. Alt: Foundry Model Ledger models tab for Sweden Central with prices in euro]

## What the Foundry Model Explorer already does

Microsoft's explorer is a region availability reference. One row per model and version, with lifecycle status, deployment SKUs, retirement date, and the list of regions that carry it, exportable to CSV. If your question is "where can I deploy gpt-5.6-terra", it is the fastest answer there is. It is also a periodic snapshot rather than a live read, it shows no prices in any currency, and it knows nothing about what you have deployed. Those gaps are the Foundry Model Ledger.

## Three sources, one Foundry Model Ledger

The model catalog lives in Azure Resource Manager. A single call, `GET /subscriptions/{id}/providers/Microsoft.CognitiveServices/locations/{region}/models`, returns every model and version a region carries, with its capabilities (chat completion, tool calling, embeddings, context window), its lifecycle status, its deployment SKUs, and its retirement date under `deprecation.inference`. This is the same data the Foundry portal shows, and it needs nothing more than Reader on the subscription.

Prices live in the Azure Retail Prices API. It is public, needs no authentication, and returns every meter Azure bills. Filter on `serviceName eq 'Foundry Models'` and a region, and you get the list prices for tokens, images, and hours, in any currency the API knows.

Your own deployments live under each Cognitive Services or Foundry account. I first reached for Azure Resource Graph, which is the natural place to list resources across a subscription. It returned zero rows against a subscription with twelve accounts. Resource Graph does not index the `accounts/deployments` child type. So the Ledger lists the accounts through ARM and calls each account's `/deployments` endpoint, six at a time.

The Foundry Model Ledger is a .NET 8 isolated Azure Function on Flex Consumption with a single-page UI, deployed with `azd up`, the same shape as the [INTERNAL LINK: RAG in 8 Steps on Azure] sample. A user-assigned managed identity with Reader on the subscription reads the first and third source. The second source needs no identity at all. Pick a region and a currency, and the table shows model, version, lifecycle, capabilities, retirement date with a days-left bar, and input and output price per million tokens. Click a row for every SKU with its capacity range, every capability ARM reports, every price meter the tool matched, and a button that checks every other region for the same model and version. A second tab joins your deployments to the catalog of their own region and sorts them by soonest retirement.

[SCREENSHOT: detail panel for gpt-5.6-terra, SKUs, matched meters and region chips visible. Alt: Foundry Model Ledger detail panel with SKUs, retirement date, matched price meters and region availability. Caption: the `area: EUR` line under capabilities is the catalog's own geography tag, not the currency switch.]

[SCREENSHOT: My deployments tab. Alt: Foundry Model Ledger deployments tab sorted by soonest retirement]

## What the data says

Sweden Central, on the day I took these screenshots, carried more than 300 catalog entries from 11 publishers. 216 were generally available, 66 in preview, 16 marked as deprecating. 70 versions retire within 90 days, and 30 are already past their retirement date but still listed. That last group matters. The catalog endpoint tells you what the region knows about, not what you can still deploy. gpt-4o-mini has been closed to new deployments for a long time and still appears. The SKU list and the retirement date are the better signals.

The same model and version often appears twice, once for account kind `OpenAI` and once for `AIServices`, each with its own SKU list. The Foundry Model Ledger merges those into one row. If you script against the endpoint yourself, expect the duplicates.

The Retail Prices API returned 1,727 meters for Foundry Models in that one region. None of them carries a model identifier.

## Prices are written for invoices

A meter name is a billing label, not a key. The same model, gpt-5.6-sol, is spread across meters such as `5.6 sol ShortCo Inp Std Gl 1M Tokens`, `5.6 sol LongCo Cd Wr PP DZ 1M Tokens`, and `56sol ShCo Cd Wr Fl Gl 1M Tokens`. Older meters read `gpt 4.1 nano cached Inp glbl Tokens` and bill per 1K tokens; newer ones bill per 1M. grok-4.6 appears as `4.6 Inp DZ Tokens` under the product `Azure Grok Models`, with no "grok" in the meter name at all.

The abbreviations are their own dialect. Input is `Inp`, `inpt`, or `in`. Output is `Outp`, `opt`, `outpt`, or `out`. Cached input is `Cd`. Global, data zone, and regional are `Gl`, `DZ`, and `regnl`. Batch, priority, and flex tiers have their own tokens.

So the Foundry Model Ledger has a matcher. It narrows meters to the model's publisher, tokenizes both sides the same way, requires every token of the model name (minus the family prefix) to appear in the meter in order, and rejects meters that carry a sibling variant such as `mini` or `pro` the model does not have. When a meter carries a date token, `o3 0416` or `chat-latest 08062026`, it pins the version. It classifies direction, deployment type, tier, and context length, normalizes the unit to a price per million tokens, and picks a headline: global standard, short context, uncached. Every price in the table carries a confidence label, exact, name, or loose, and the detail panel shows every matched meter, so the headline number is never the only evidence.

On the first live run it priced 204 of the entries in Sweden Central. The misses were instructive. FLUX image models came out at "40,000 per million" because their meters bill per 1K images and I had treated every 1K unit as tokens. Qwen sits under product `Qwen models`, which the publisher map did not know, and most of its meters are fine-tuning meters that must never become a headline price. And the Anthropic models, plus Cohere rerank and parse, are in the catalog with no Foundry Models meter in the region at all. The model exists; the public price does not.

[SCREENSHOT: Price meters tab, search "5.6 sol". Alt: Foundry Model Ledger price meters tab showing raw Azure Retail Prices rows for gpt-5.6-sol]

## Where the Foundry Model Ledger is the wrong answer

Do not budget on it. These are list prices, matched heuristically, in whatever currency you pick. The matcher will be wrong somewhere the day Microsoft renames a meter, and it already cannot price provisioned throughput, which is billed per hour per unit rather than per model. Use it to see the shape of a decision, then confirm on the pricing page.

Do not put it on the internet as is. The Function has no authentication, and it exposes your deployment list. Put Easy Auth in front of it or keep it internal, or front it with API Management the way the [INTERNAL LINK: APIM for AI Workloads series] does for model endpoints.

And do not read the catalog as an availability promise. Listed does not mean deployable, and a retirement date is a floor, not a schedule. For a plain "where is it available" question, Microsoft's explorer remains the quicker tool.

## What I would build next

The tab I did not plan is the one people ask about: my deployments, joined to the catalog, sorted by retirement. That is a governance question, not a browsing question, and it sits next to the routing questions from the [INTERNAL LINK: AI control plane post on Model Router]. The natural next steps are all on that side. All subscriptions in a tenant instead of one. An alert when a deployment sits on a version retiring within 90 days. A history, so you can see what appeared and what closed in a region since last month, which is a changelog Microsoft does not publish. And a cost delta for moving a deployment to its successor, using the same matcher.

The Foundry Model Ledger repo is at [github.com/steefjan1/foundry-model-ledger](https://github.com/steefjan1/foundry-model-ledger). `azd up` deploys it; `scripts/run-local.ps1` runs it against your `az login`; `node tools/mock-server.mjs` runs the UI on a sample snapshot without .NET or Azure. If the matcher misprices a model in your region, the detail panel shows why, and an issue with that screenshot is the fastest way to get it fixed.
