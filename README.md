# Cash Projection

A modern WPF desktop application for managing and projecting cash flow over time. Track your recurring transactions, visualize your account balance, and identify potential cash flow issues before they happen.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![WPF](https://img.shields.io/badge/WPF-Windows-0078D4?logo=windows)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

### ?? Cash Flow Management
- Track deposits and payments with automatic balance calculation
- Support for recurring transactions (Monthly, Quarterly, Semi-Annually, Annually)
- Special "Two Weeks Past Last" periodicity for cases where you're paid every two weeks
- Visual warning indicators for low and critically low projected balances

### ?? Smart Visualization
- Color-coded transaction rows (warnings for low balance, errors for negative balance)
- Future transactions displayed with reduced opacity
- Low balance indicator highlighting the lowest balance within �1 month of today
- Running balance calculation across all transactions

### ? Productivity Features
- **Keyboard Shortcuts**:
  - `Ctrl+F` - Find transactions
  - `F2` - Edit the selected projection item
- **Quick Actions**:
  - Edit: open the selected projection item in a popover editor — Save writes it to the database immediately (also Delete from there)
  - Push Forward: Move recurring transactions by their periodicity
  - Add new transactions via the same popover editor
- **Smart Search**: Find transactions by name with instant results and keyboard navigation, newest first

### ?? Data Management
- Local SQLite database, stored in OneDrive (with fallback to Documents folder)
- Every add/edit/delete/push-forward writes through immediately — there's no separate save step or pending-changes state

## Tech Stack

- **Framework**: .NET 10.0 (Windows)
- **UI Framework**: WPF (Windows Presentation Foundation)
- **MVVM**: CommunityToolkit.Mvvm 8.4.0
- **Design**: Material Design In XAML Toolkit 5.3.0
- **Architecture**: MVVM pattern with proper separation of concerns

## Getting Started

### Prerequisites

- Windows 10/11
- .NET 10.0 SDK or later
- Visual Studio 2022 (recommended) or VS Code with C# extension

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/larrye11ison/CashProjection.git
   cd CashProjection
   ```

2. Build the solution:
   ```bash
   dotnet build
   ```

3. Run the application:
   ```bash
   dotnet run --project CashProjection
   ```

### First Launch

On first launch, you'll be walked through connecting a bank account via Plaid — this is required before the app shows anything, since it's how the account balance and real transactions get in. After that, you can add, modify, or delete recurring projection items — each change is written to the database as soon as you make it.

## Usage

### Adding, Editing, and Deleting Transactions

The projection grid itself is read-only — all changes go through a popover editor:

1. Click **Add New** in the toolbar to create a transaction, or select an existing one and click **Edit** (or press `F2`)
2. Enter the transaction details:
   - **Name**: Description of the transaction
   - **Date**: Transaction date
   - **Deposit** or **Payment**: Enter the amount (mutually exclusive)
   - **Periodicity**: Select how often this transaction recurs
   - **Match pattern**: Optional text used instead of the name when matching this item to a real bank transaction
3. Click **Save** (or press `Enter`) to write it to the database and close the editor, or **Delete** to remove an existing item (with confirmation)

### Managing Recurring Transactions

Select a transaction and click **Push Forward** in the toolbar to automatically advance it by its periodicity:
- Monthly transactions move forward 1 month
- Quarterly transactions move forward 3 months
- "Two weeks past last" will find the last transaction whose name matches the item being "pushed," then will reschedule it for two weeks beyond that. This is typically used for cases where you get a paycheque every two weeks.

### Finding Transactions

1. Press `Ctrl+F` or click the **Find** button
2. Type one or more keywords separated by spaces to search transaction names
3. Use arrow keys to navigate results
4. Press `Enter` to jump to the selected transaction

## Architecture

### Key Design Patterns

- **MVVM**: Clean separation between UI and business logic
- **MVVM Toolkit**: Using `[ObservableProperty]` and `[RelayCommand]` for boilerplate reduction

## Contributing

This is a tool that I created for exactly one person - *me*! It does exactly what I need it to do and nothing more, which is a large part of the appeal for me. But if you're interested in contributing, please feel free to reach out to me through github.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Material Design In XAML Toolkit](http://materialdesigninxaml.net/) for styling
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for modern MVVM patterns
- The .NET and WPF communities for excellent documentation and support

## Contact

(I am not) Larry Ellison - [@larrye11ison](https://github.com/larrye11ison)

Project Link: [https://github.com/larrye11ison/CashProjection](https://github.com/larrye11ison/CashProjection)

---

? If you find this project useful, please consider giving it a star on GitHub!
