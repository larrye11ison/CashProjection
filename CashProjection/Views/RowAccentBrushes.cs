using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    // Resolves a ProjectionItemViewModel.Accent into an actual
    // Background/Foreground pair for AccountProjectionView's DataGrid rows.
    // Looked up from Application.Current.Resources each time rather than
    // cached, both because that's the theme-aware lookup path (it tracks
    // the OS/app light-dark setting live) and because a bad/renamed
    // resource key just falls back to a literal color instead of crashing.
    internal static class RowAccentBrushes
    {
        public static (Brush? Background, Brush? Foreground) Resolve(RowAccent accent) => accent switch
        {
            RowAccent.OverdueUnmatched => (
                TryGet("SystemFillColorCautionBrush", Color.FromArgb(255, 157, 93, 0)),
                new SolidColorBrush(Colors.Black)),

            // A deliberately different (dark gold/olive) color from the
            // caution brush above, even though both are "overdue"-flavored
            // warnings — this one specifically means "the bank's already
            // moved past this, unlike an ordinary overdue projection" and
            // is worth telling apart at a glance. Literal, not a theme
            // resource: chosen dark enough that white text stays readable
            // in either theme, so it deliberately never flips with it.
            RowAccent.PendingBankActivity => (
                new SolidColorBrush(Color.FromArgb(255, 128, 96, 0)),
                new SolidColorBrush(Colors.White)),

            // No background here on purpose — the Link glyph in the Flags
            // column already marks a matched row, and the translucent
            // accent fill this used to apply read as a jarring light-blue
            // flash across the row rather than a subtle tint.
            RowAccent.Matched => (null, null),

            // Bank transactions vs. projection items no longer get a
            // different row background at all — the Calendar glyph in the
            // Flags column is the only thing distinguishing them now.
            _ => (null, null),
        };

        internal static Brush TryGet(string key, Color fallback) =>
            Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
                ? brush
                : new SolidColorBrush(fallback);
    }
}
