# Add New Transaction and Delete Transaction Features

Resolves #1

## Summary

This PR implements the ability to add new transactions and delete existing transactions as requested in issue #1.

## Changes Made

### 1. Add New Transaction Feature

**Backend (`AccountProjectionViewModel.cs`):**
- Added `AddNew()` method that:
  - Creates a new transaction with today's date as the default
  - Sets sensible defaults (empty name, no deposit/payment, Monthly periodicity)
  - Automatically adds the transaction to the collection
  - Focuses the new transaction in the grid with the Date field in edit mode for immediate editing
  - Marks the data as dirty for save tracking

**Frontend (`ShellView.xaml`):**
- Added "Add New" button to the toolbar
- Positioned at the beginning of the toolbar for easy access
- Uses MaterialDesign styling consistent with other buttons
- Includes tooltip: "Add a new transaction"

### 2. Delete Transaction Feature

**Backend (`AccountProjectionViewModel.cs`):**
- Added `DeleteTransaction(TransactionItemViewModel)` method that:
  - Displays a confirmation dialog with transaction details
  - Shows transaction name, amount (deposit or payment), and date
  - Uses exact wording per spec: "Are you sure you want to delete '[name]' [deposit/payment of amount] on [date]?"
  - Only removes the transaction if user confirms (Yes button)
  - Marks the data as dirty after deletion

**Frontend (`AccountProjectionView.xaml`):**
- Added Delete button to the Actions column in the DataGrid
- Positioned to the right of the existing "Push Forward" button
- Uses MaterialDesign `PackIcon` with `Kind="Delete"` (trash can icon)
- Styled with red foreground color to indicate destructive action
- Includes tooltip: "Delete this transaction"
- Both buttons are wrapped in a horizontal StackPanel for proper layout

## Technical Details

- All changes follow existing code patterns and conventions
- Uses Caliburn.Micro's `cal:Message.Attach` for MVVM binding
- Maintains consistency with MaterialDesign UI components
- Properly integrates with existing dirty tracking and save functionality
- Build verified successfully with no compilation errors

## Testing Recommendations

1. **Add New Transaction:**
   - Click "Add New" button in toolbar
   - Verify new transaction appears at the bottom of the grid
   - Verify the Date field is focused and in edit mode
   - Verify today's date is pre-filled
   - Edit the transaction details and save

2. **Delete Transaction:**
   - Click the Delete button (trash icon) on any transaction
   - Verify confirmation dialog appears with correct transaction details
   - Test "Yes" to confirm deletion
   - Test "No" to cancel deletion
   - Verify deleted transactions are removed from the grid
   - Verify data is marked as dirty after deletion

## Screenshots

The UI changes include:
- New "Add New" button in the toolbar (before Save button)
- Delete icon button in Actions column (after Push Forward button)

## Related Issues

Closes #1
