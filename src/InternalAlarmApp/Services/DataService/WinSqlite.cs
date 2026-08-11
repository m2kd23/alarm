using System.Runtime.InteropServices;
using System.Text;

namespace InternalAlarmApp.Services.DataService;

internal sealed class WinSqliteConnection : IDisposable
{
    private const int Ok = 0;
    private const int Row = 100;
    private const int Done = 101;
    private static readonly IntPtr Transient = new(-1);
    private IntPtr _db;

    public WinSqliteConnection(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rc = Native.sqlite3_open_v2(Utf8(path), out _db, 0x00000002 | 0x00000004 | 0x00010000, IntPtr.Zero);
        if (rc != Ok) throw Error("SQLite 데이터베이스를 열 수 없습니다", rc);
        Native.sqlite3_busy_timeout(_db, 5000);
        Execute("PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
    }

    public long LastInsertRowId => Native.sqlite3_last_insert_rowid(_db);

    public void Execute(string sql, params object?[] values)
    {
        using var command = Prepare(sql, values);
        var rc = Native.sqlite3_step(command.Handle);
        if (rc != Done && rc != Row) throw Error("SQLite 명령 실행 오류", rc);
    }

    public List<T> Query<T>(string sql, Func<SqliteRow, T> map, params object?[] values)
    {
        using var command = Prepare(sql, values);
        var result = new List<T>();
        while (true)
        {
            var rc = Native.sqlite3_step(command.Handle);
            if (rc == Done) return result;
            if (rc != Row) throw Error("SQLite 조회 오류", rc);
            result.Add(map(new SqliteRow(command.Handle)));
        }
    }

    public T? Scalar<T>(string sql, params object?[] values)
    {
        using var command = Prepare(sql, values);
        var rc = Native.sqlite3_step(command.Handle);
        if (rc == Done) return default;
        if (rc != Row) throw Error("SQLite 조회 오류", rc);
        object? value = typeof(T) == typeof(long) ? Native.sqlite3_column_int64(command.Handle, 0) : new SqliteRow(command.Handle).Text(0);
        return (T?)value;
    }

    private Statement Prepare(string sql, object?[] values)
    {
        var bytes = Utf8(sql);
        var rc = Native.sqlite3_prepare_v2(_db, bytes, bytes.Length - 1, out var stmt, IntPtr.Zero);
        if (rc != Ok) throw Error("SQLite 쿼리 준비 오류", rc);
        var command = new Statement(stmt);
        try
        {
            for (var i = 0; i < values.Length; i++) Bind(stmt, i + 1, values[i]);
            return command;
        }
        catch { command.Dispose(); throw; }
    }

    private void Bind(IntPtr stmt, int index, object? value)
    {
        var rc = value switch
        {
            null => Native.sqlite3_bind_null(stmt, index),
            int i => Native.sqlite3_bind_int(stmt, index, i),
            long l => Native.sqlite3_bind_int64(stmt, index, l),
            bool b => Native.sqlite3_bind_int(stmt, index, b ? 1 : 0),
            DateTime d => BindText(stmt, index, d.ToString("O")),
            _ => BindText(stmt, index, value.ToString() ?? "")
        };
        if (rc != Ok) throw Error("SQLite 값 바인딩 오류", rc);
    }

    private static int BindText(IntPtr stmt, int index, string value)
    {
        var bytes = Utf8(value);
        return Native.sqlite3_bind_text(stmt, index, bytes, bytes.Length - 1, Transient);
    }

    private Exception Error(string message, int rc)
    {
        var ptr = _db == IntPtr.Zero ? IntPtr.Zero : Native.sqlite3_errmsg(_db);
        var detail = ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr);
        return new InvalidOperationException($"{message} ({rc}): {detail}");
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + '\0');

    public void Dispose()
    {
        if (_db != IntPtr.Zero) { Native.sqlite3_close_v2(_db); _db = IntPtr.Zero; }
    }

    private sealed class Statement(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public void Dispose() => Native.sqlite3_finalize(Handle);
    }

    internal static class Native
    {
        private const string Dll = "winsqlite3.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_null(IntPtr stmt, int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_int(IntPtr stmt, int index, int value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int bytes, IntPtr destructor);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_int(IntPtr stmt, int col);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_bytes(IntPtr stmt, int col);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_type(IntPtr stmt, int col);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern long sqlite3_last_insert_rowid(IntPtr db);
    }
}

internal readonly struct SqliteRow(IntPtr statement)
{
    public long Int64(int column) => WinSqliteConnection.Native.sqlite3_column_int64(statement, column);
    public int Int32(int column) => WinSqliteConnection.Native.sqlite3_column_int(statement, column);
    public bool Bool(int column) => Int32(column) != 0;
    public string Text(int column)
    {
        var ptr = WinSqliteConnection.Native.sqlite3_column_text(statement, column);
        if (ptr == IntPtr.Zero) return "";
        var length = WinSqliteConnection.Native.sqlite3_column_bytes(statement, column);
        return Marshal.PtrToStringUTF8(ptr, length) ?? "";
    }
    public DateTime DateTime(int column) => System.DateTime.Parse(Text(column), null, System.Globalization.DateTimeStyles.RoundtripKind);
    public DateTime? NullableDateTime(int column) => WinSqliteConnection.Native.sqlite3_column_type(statement, column) == 5 ? null : DateTime(column);
}
