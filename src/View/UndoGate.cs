using System;
using System.Reflection;
using System.Windows.Controls;

namespace Marabook.View
{
    /// <summary>Pauses the RichTextBox's undo recording while the pagination
    /// engine adjusts paragraph margins — those are presentation, not content,
    /// and must never pollute the writer's Ctrl+Z.
    ///
    /// WPF offers no public switch that does not clear the stack, so this
    /// reaches MS.Internal.Documents.UndoManager by reflection (probed on
    /// .NET Framework 4.8: the stack survives, paused changes are unrecorded).
    /// If the internals ever change, the gate degrades to a no-op: pagination
    /// keeps working, undo merely records extra margin units.</summary>
    public static class UndoGate
    {
        private static bool _resolved;
        private static MethodInfo _getManager;
        private static PropertyInfo _isEnabled;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                var type = typeof(RichTextBox).Assembly.GetType("MS.Internal.Documents.UndoManager");
                if (type == null) return;
                _getManager = type.GetMethod("GetUndoManager",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                _isEnabled = type.GetProperty("IsEnabled",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            }
            catch
            {
                _getManager = null;
                _isEnabled = null;
            }
        }

        /// <summary>Runs an action with undo recording paused.</summary>
        public static void Paused(RichTextBox box, Action action)
        {
            Resolve();
            object manager = null;
            if (_getManager != null && _isEnabled != null)
            {
                try { manager = _getManager.Invoke(null, new object[] { box }); }
                catch { manager = null; }
            }
            if (manager == null)
            {
                action();
                return;
            }
            try { _isEnabled.SetValue(manager, false, null); }
            catch { action(); return; }
            try
            {
                action();
            }
            finally
            {
                try { _isEnabled.SetValue(manager, true, null); }
                catch { }
            }
        }
    }
}
