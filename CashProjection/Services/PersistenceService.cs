using System.IO;
using System.Text.Json;
using CashProjection.Models;
using CashProjection.ViewModels;

namespace CashProjection.Services;

public sealed class TransactionState
{
    public string Name { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public decimal? Deposit { get; set; }
    public decimal? Payment { get; set; }
    public Periodicity Periodicity { get; set; }
}

public sealed class AccountState
{
    public string AccountName { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public List<TransactionState> Transactions { get; set; } = [];
}

public static class PersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetDefaultFilePath()
    {
        // Prefer OneDrive, fallback to Documents
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        var baseDir = !string.IsNullOrWhiteSpace(oneDrive)
            ? oneDrive!
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        return Path.Combine(baseDir, "CashProjection.json");
    }

    public static void Save(AccountProjectionViewModel vm)
    {
        var state = new AccountState
        {
            AccountName = vm.AccountName,
            InitialBalance = vm.InitialBalance,
            Transactions = vm
                .Transactions
                .Select(t => new TransactionState
                {
                    Name = t.Name,
                    TransactionDate = t.TransactionDate,
                    Deposit = t.Deposit,
                    Payment = t.Payment,
                    Periodicity = t.Periodicity,
                })
                .ToList(),
        };

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var path = GetDefaultFilePath();
        File.WriteAllText(path, json);
    }

    public static AccountState? Load()
    {
        var path = GetDefaultFilePath();
        if (!File.Exists(path))
        {
			path = Path.Combine(AppContext.BaseDirectory, "SampleData.json");
            if (!File.Exists(path))
            {
                return null;
			}
		}

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AccountState>(json, JsonOptions);
    }
}
