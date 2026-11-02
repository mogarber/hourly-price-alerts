# Hourly Price Alerts

A free, open-source iPhone app for [ComEd Hourly Pricing](https://hourlypricing.comed.com/) customers. See the live price and get a push notification when it crosses a limit you set.

> **Status:** in development, not yet on the App Store.
>
> Not affiliated with or endorsed by ComEd or Exelon.

## Features

- Current hour average, latest 5-minute price, and a 24-hour chart
- Alerts when the price goes above or below a limit, or moves by a set amount
- Hourly average by default, since that's what ComEd bills on, with 5-minute alerts as an option
- Cooldowns and quiet hours, so a choppy market doesn't flood your phone
- No account. The server stores only an install ID, push token, time zone and your alert rules, and you can delete them from the app

## How it works

The app reads prices directly from ComEd's public feed. A small Azure Function checks that feed every minute, evaluates alert rules when a new price appears, and sends notifications through Apple's push service. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Development

### Backend

Requires the .NET 10 SDK.

```bash
dotnet test backend/HourlyPriceAlerts.sln
```

Azure resources are defined in [`infra/`](infra/).

### iPhone app

Requires Xcode and [XcodeGen](https://github.com/yonaskolb/XcodeGen).

```bash
cd ios && xcodegen generate && open HourlyPriceAlerts.xcodeproj
```

To run on a device or use push notifications, copy `ios/Config/Local.xcconfig.example` to `ios/Config/Local.xcconfig` and fill in your team and API URL. Launch with the `-MockBackend` argument to try the alert screens without a server.

## Support

Hourly Price Alerts is free. If it saves you money, you can help cover the server costs:

<a href="https://www.buymeacoffee.com/mogarber"><img src="https://img.buymeacoffee.com/button-api/?text=Buy%20me%20a%20coffee&emoji=&slug=mogarber&button_colour=FFDD00&font_colour=000000&font_family=Cookie&outline_colour=000000&coffee_colour=ffffff" alt="Buy me a coffee" height="50"></a>

## License

[MIT](LICENSE)
