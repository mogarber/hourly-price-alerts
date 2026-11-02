# Privacy policy

**Hourly Price Alerts** · Effective November 2, 2026

Hourly Price Alerts is a free app that shows ComEd Hourly Pricing data and can notify you when the price crosses a limit you set. It has no accounts, ads, analytics or tracking. It is not affiliated with ComEd.

## If you only view prices

The app downloads prices directly from ComEd's public price feed. Nothing is sent to our alert service. As with any website or app, ComEd's servers can see your IP address when your iPhone requests the data.

## If you create alerts

To send notifications, our alert service stores:

- A random install ID created by the app
- A one-way hash of a random secret that protects your alerts from other people
- The push notification token Apple issues for your iPhone
- Your iPhone's time zone, so quiet hours and notification times are correct
- Your alert settings, and when each alert last fired

We do not collect your name, email address, phone number, location, ComEd account details or electricity usage.

## How it is used

This data is used only to decide when an alert should fire and to deliver it. It is not sold, shared for advertising, or used to track you.

## Who processes it

- **Microsoft Azure** hosts the alert service and stores the data above. Service logs, which can include install IDs, are kept for up to 30 days to diagnose problems.
- **Apple** delivers notifications through the Apple Push Notification service, using your push token and the notification's text.

## Keeping and deleting your data

The data stays until you remove it or Apple tells us your push token is no longer valid, at which point we delete your device and its alerts. To delete it yourself, open **Settings** in the app and tap **Delete my data**. This removes your device and alerts from the alert service and clears saved data on your iPhone. Deleting the app alone stops notifications, but your data stays until the token is reported as invalid.

## Children

The app is not directed at children and does not knowingly collect their personal information.

## Changes and contact

Changes to this policy are published here, with the full history in this repository. For questions or requests, [open an issue](https://github.com/mogarber/hourly-price-alerts/issues).
