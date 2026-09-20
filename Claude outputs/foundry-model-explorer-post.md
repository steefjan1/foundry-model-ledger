# Which Model, Where, at What Price, Until When: An Azure Foundry Model Explorer

Every model conversation with a team ends with the same four questions. Is the model available in our region? What can it do? What does it cost per million tokens? And when does Microsoft retire it? Azure answers all four. It just answers them in three places that were never designed to be read together. So I built a small tool that reads them together, and the interesting part turned out to be what the data looks like once you do.

[SCREENSHOT: Models tab, swedencentral, prices in EUR, gpt-5.6 filter]

## Three sources, one table

The model catalog lives in Azure Resource Manager. A single call, `GET /subscriptions/{id}/providers/Microsoft.CognitiveServices/locations/{region}/models`, returns every model and version a region carries, with its capabilities (chat completion, tool calling, embeddings, context window), its lifecycle status, its deployment SKUs, and its retirement date under `deprecation.inference`. This is the same data the Foundry portal shows, and it needs nothing more than Reader on the subscription.

Prices live in the Azure Retail Prices API. It is public, needs no authentication, and returns every meter Azure bills. Filter on `serviceName eq 'Foundry Models'` and a region, and you get the list prices for tokens, images, and hours.

Your own deployments live in Azure Resource Graph. One KQL query over `microsoft.cognitiveservices/accounts/deployments` lists every deployment in the subscription with its model name and version.

The explorer is a .NET 8 isolated Azure Function on Flex Consumption with a single-page UI, deployed with `azd up`. A user-assigned managed identity with Reader on the subscription reads the first and third source. The second source needs no identity at all. Pick a region, pick a currency, and the table shows model, version, lifecycle, capabilities, retirement date with a days-left bar, and input and output price per million tokens. Click a row and you see every SKU with its capacity range, every capability ARM reports, and every price meter the tool matched, with the raw JSON at the bottom. A second tab joins your deployments to the catalog of their own region and sorts them by soonest retirement.

[SCREENSHOT: detail panel for one model, meters table visible]

## What the data says

Sweden Central, on the day I took these screenshots, carried more than 300 catalog entries from 11 publishers. 216 were generally available, 66 in preview, 16 marked as deprecating. 70 versions retire within 90 days, and 30 are already past their retirement date but still listed. That last group matters. The catalog endpoint tells you what the region knows about, not what you can still deploy. gpt-4o-mini has been closed to new deployments for a long time and still appears. The SKU list and the retirement date are the better signals.

The same model and version often appears twice, once for account kind `OpenAI` and once for `AIServices`, each with its own SKU list. The explorer merges those into one row. If you script against the endpoint yourself, expect the duplicates.

The Retail Prices API returned 1,727 meters for Foundry Models in that one region. None of them carries a model identifier.

## Prices are written for invoices

A meter name is a billing label, not a key. The same model, gpt-5.6-sol, is spread across meters such as `5.6 sol ShortCo Inp Std Gl 1M Tokens`, `5.6 sol LongCo Cd Wr PP DZ 1M Tokens`, and `56sol ShCo Cd Wr Fl Gl 1M Tokens`. Older meters read `gpt 4.1 nano cached Inp glbl Tokens` and bill per 1K tokens; newer ones bill per 1M. grok-4.6 appears as `4.6 Inp DZ Tokens` under the product `Azure Grok Models`, with no "grok" in the meter name at all. Input is `Inp`, `inpt`, or `in`; output is `Outp`, `opt`, `outpt`, or `out`. Cached input is `Cd`. Global, data zone, and regional are `Gl`, `DZ`, and `regnl`. Batch, priority, and flex tiers have their own abbreviations.

So the tool has a matcher. It narrows meters to the model's publisher, tokenizes both sides the same way, requires every token of the model name (minus the family prefix) to appear in the meter in order, and rejects meters that carry a sibling variant such as `mini` or `pro` the model does not have. When a meter carries a date token, `o3 0416` or `chat-latest 08062026`, it pins the version. It classifies direction, deployment type, tier, and context length, normalizes the unit to a price per million tokens, and picks a headline: global standard, short context, uncached. Every price in the table carries a confidence label, exact, name, or loose, and the detail panel shows every matched meter, so the headline number is never the only evidence.

On the first live run it priced 204 of the entries in Sweden Central. The misses were instructive. FLUX image models came out at "40,000 per million" because their meters bill per 1K images and I had treated every 1K unit as tokens. Qwen sits under product `Qwen models`, which the publisher map did not know, and most of its meters are fine-tuning meters that must never become a headline price. And the Anthropic models, plus Cohere rerank and parse, are in the catalog with no Foundry Models meter in the region at all. The model exists; the public price does not.

[SCREENSHOT: Price meters tab, search "5.6 sol"]

## Where this is the wrong answer

Do not budget on it. These are list prices, matched heuristically, in whatever currency you pick. The matcher will be wrong somewhere the day Microsoft renames a meter, and it already cannot price provisioned throughput, which is billed per hour per unit rather than per model. Use it to see the shape of a decision, then confirm on the pricing page.

Do not put it on the internet as is. The Function has no authentication, and it exposes your deployment list. Put Easy Auth in front of it or keep it internal.

And do not read the catalog as an availability promise. Listed does not mean deployable, and a retirement date is a floor, not a schedule.

## What I would build next

The tab I did not plan is the one people ask about: my deployments, joined to the catalog, sorted by retirement. That is a governance question, not a browsing question. The natural next steps are all on that side. All subscriptions in a tenant instead of one. An alert when a deployment sits on a version retiring within 90 days. A history, so you can see what appeared and what closed in a region since last month, which is a changelog Microsoft does not publish. And a cost delta for moving a deployment to its successor, using the same matcher.

The repo is at [github.com/steefjan1/foundry-model-explorer](https://github.com/steefjan1/foundry-model-explorer). `azd up` deploys it; `scripts/run-local.ps1` runs it against your `az login`; `node tools/mock-server.mjs` runs the UI on a sample snapshot without .NET or Azure. If the matcher misprices a model in your region, the detail panel shows why, and an issue with that screenshot is the fastest way to get it fixed.
