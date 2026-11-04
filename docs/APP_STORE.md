# App Store listing

The text entered in App Store Connect.

| Field | Value |
|---|---|
| Name | Hourly Price Alerts |
| Subtitle | For ComEd hourly customers |
| Bundle ID | io.github.mogarber.hourlypricealerts |
| SKU | hourlypricealerts |
| Primary category | Utilities |
| Secondary category | Lifestyle |
| Price | Free |
| Availability | United States |
| Age rating | 4+ (no restricted content) |
| Support URL | https://github.com/mogarber/hourly-price-alerts/issues |
| Marketing URL | https://github.com/mogarber/hourly-price-alerts |
| Privacy policy URL | https://github.com/mogarber/hourly-price-alerts/blob/main/docs/PRIVACY.md |

## Keywords

```
electricity,energy,power,kWh,rate,real-time,hourly pricing,Illinois,Chicago,notification,utility
```

"ComEd" stays out of the keywords field, because Apple advises against trademarked terms there.

## Promotional text

```
See the live electricity price and get notified when it goes above or below your limit. Free, with no account.
```

## Description

```
On ComEd's Hourly Pricing program, your electricity price changes all day. Hourly Price Alerts shows you the current price and notifies you when it matters, so you know when to run the dryer and when to wait.

LIVE PRICES
• Current hour average, the number ComEd bills on
• Latest 5-minute price
• 24-hour chart by hour or by 5-minute reading
• Low, moderate, elevated and high labels at a glance, including negative prices

ALERTS YOU CONTROL
• Notify me when the price goes above or below a limit
• Notify me when the price moves by a set amount
• Use the steadier hourly average, or the 5-minute price
• Set a wait between alerts, plus quiet hours for overnight

PRIVATE AND FREE
• No account, no ads, no tracking
• No name, email or location collected
• Delete your data from the app at any time
• Open source

Hourly Price Alerts is an independent app. It is not affiliated with or endorsed by ComEd or Exelon. Prices come from ComEd's public Hourly Pricing data and are for information only. Your ComEd bill is the final word on what you pay. Requires enrollment in ComEd's Hourly Pricing program for the prices to apply to your bill.
```

## Review notes

```
PURPOSE AND AUDIENCE
Hourly Price Alerts is a free, independent app for customers of ComEd's Hourly Pricing program (northern Illinois). On that program the electricity price changes every 5 minutes. The app shows the current price and notifies the user when it goes above or below a limit they set, or moves by an amount they set, so they can shift usage such as laundry or EV charging to cheaper times. Audience: adults enrolled in ComEd Hourly Pricing.

HOW TO TEST (no account, no login, no sample files)
1. Open the app. The Prices tab shows live prices.
2. Alerts tab > + > "Alert me when": Price goes below > Using: 5-minute price > Price: 100 > Save. Allow notifications when asked.
3. A notification arrives when the next price is published, usually within 10 minutes. (Prices are almost always under 100 cents, so this alert always fires.)
4. Settings > "Send test notification" delivers one immediately.
5. Settings > "Delete my data" removes the device and its alerts from our server and clears the app's saved data.
There is no account registration, user-generated content, or paid content, so no registration, reporting or blocking flows apply.

SCREEN RECORDING
Recorded on a physical iPhone running the latest iOS, starting from app launch. It is attached in the Attachment field of this section.

EXTERNAL SERVICES
- ComEd Hourly Pricing public API (hourlypricing.comed.com): the price data. The app reads it directly. No key or login.
- Microsoft Azure (Functions, Table Storage): our server checks ComEd prices every minute, decides when an alert should fire, and stores an install ID, push token, time zone and the user's alert settings. No names, emails or location.
- Apple Push Notification service: delivers the alerts.
No other third-party services, SDKs, analytics, ads, authentication or payment services.

REGIONS
The app works the same everywhere it is available. We list it for the US only, because ComEd serves northern Illinois. The prices matter only to ComEd Hourly Pricing customers.

THIRD-PARTY MATERIAL
The only third-party material is ComEd's published price data. The app includes no ComEd logos, trademarks or text. ComEd publishes the data through a public API with published terms (https://hourlypricing.comed.com/comed-hourly-pricing-apis-terms-of-service/) that needs no registration or credentials. The app says on its Settings screen and in its listing that it is not affiliated with ComEd. We have no separate agreement with ComEd.

NOT A REGULATED INDUSTRY
The app gives information only and no financial, medical or legal services.
```

## App privacy answers

| Question | Answer |
|---|---|
| Data used to track you | None |
| Identifiers, Device ID (install ID and push token) | Collected for App Functionality, linked to identity, not used for tracking |
| Other Data (time zone and alert settings) | Collected for App Functionality, linked to identity, not used for tracking |
| Everything else | Not collected |

## Screenshots

Taken on the iPhone 18 Pro simulator (1206 x 2622), the size App Store Connect asks for. It scales them for other iPhones.
