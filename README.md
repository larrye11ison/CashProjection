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
  - `Ctrl+B` - Focus on Initial Balance field
  - `Ctrl+S` - Save to OneDrive/Documents
  - `Ctrl+F` - Find transactions
- **Quick Actions**:
  - Push Forward: Move recurring transactions by their periodicity
  - Delete with confirmation
  - Add new transactions with auto-focus
- **Smart Search**: Find transactions by name with instant results and keyboard navigation

### ?? Data Management
- Automatic save to OneDrive (with fallback to Documents folder)
- JSON-based storage format
- Sample data included for quick start
- Auto-save prompt on exit if changes are pending

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

On first launch, the application will load sample transaction data. You can:
- Edit the account name and initial balance
- Add, modify, or delete transactions
- Use `Ctrl+S` to save your data to OneDrive/Documents

## Usage

### Adding Transactions

1. Click the **Add New** button in the toolbar (or add a row at the bottom of the grid)
2. Enter the transaction details:
   - **Date**: Transaction date
   - **Name**: Description of the transaction
   - **Deposit** or **Payment**: Enter the amount (mutually exclusive)
   - **Periodicity**: Select how often this transaction recurs

### Managing Recurring Transactions

Use the **Push Forward** button (?) to automatically advance a transaction by its periodicity:
- Monthly transactions move forward 1 month
- Quarterly transactions move forward 3 months
- "Two weeks past last" will find the last transaction whose name matches the item being "pushed," then will reschedule it for two weeks beyond that. This is typically used for cases where you get a paycheque every two weeks.

### Finding Transactions

1. Press `Ctrl+F` or click the **Find** button
2. Type one or more keywords separated by spaces to search transaction names
3. Use arrow keys to navigate results
4. Press `Enter` to jump to the selected transaction

## Architecture

### Project Structure

```
CashProjection/
??? Assets/              # Application icons and resources
??? Behaviors/           # WPF attached behaviors
?   ??? GridViewColumnResize.cs
?   ??? SelectAllOnFocusBehavior.cs
??? Models/              # Domain models and converters
?   ??? Transaction.cs
?   ??? Converters.cs
?   ??? Periodicity.cs (enum)
??? Services/            # Business logic and persistence
?   ??? PersistenceService.cs
?   ??? TransactionParser.cs
??? ViewModels/          # MVVM ViewModels
?   ??? ShellViewModel.cs
?   ??? AccountProjectionViewModel.cs
?   ??? TransactionItemViewModel.cs
??? Views/               # WPF Views
?   ??? ShellView.xaml
?   ??? AccountProjectionView.xaml
??? App.xaml             # Application entry point
```

### Key Design Patterns

- **MVVM**: Clean separation between UI and business logic
- **MVVM Toolkit**: Using `[ObservableProperty]` and `[RelayCommand]` for boilerplate reduction
- **Repository Pattern**: `PersistenceService` handles all data storage
- **DTO Pattern**: `Transaction` model separates data transfer from ViewModels

## Data Storage

Transaction data is stored in JSON format at:
- Primary: `%OneDrive%\CashProjection.json`
- Fallback: `%USERPROFILE%\Documents\CashProjection.json`

### Data Format

```json
{
  "AccountName": "My Account",
  "InitialBalance": 5000.00,
  "Transactions": [
    {
      "Name": "Salary",
      "TransactionDate": "2025-01-01",
      "Deposit": 3000.00,
      "Payment": null,
      "Periodicity": "Monthly"
    }
  ]
}
```

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
