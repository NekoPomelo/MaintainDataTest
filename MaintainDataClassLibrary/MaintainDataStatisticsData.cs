using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MaintainDataClassLibrary
{
    //维护数据统计表专有名词枚举
    internal enum MaintainDataName
    {
        SummaryTable,//总表
        MaintainDataTable,//维护数据表
        DateTable,//日期表
        DateTime,//日期
        TotalRunningTime,//累计运行时间
        TotalAssistanceTime,//累计协助时间
        TotalNumberOfRepairs,//累计维修次数
        TotalNumberOfAssistance,//累计协助次数
        TimestampType,//时间戳类型 ，正常运行，维修，协助
        SatrtTime,//开始时间
        EndTime,//结束时间
        RunningTime,//正常运行类型
        AssistanceTime, //协助类型
        RepairTime,//维修类型
        DayRunningTime,//当日运行时间
        DayAssistanceTime,//当日协助时间
        DayNumberOfRepairs,//当日维修次数
        DayNumberOfAssistance,//当日协助次数
        BeforeRunningTime,//当日之前运行时间
        BeforeAssistanceTime,//当日之前协助时间
        BeforeNumberOfRepairs,//当日之前维修次数
        BeforeNumberOfAssistance,//当日之前协助次数
        BeforeMTBF,
        BeforeMTBA,
        BeforeMTTA,
    }

    //MTBF:平均故障间隔时间 >1000h
    //MTBA ：平均间隔辅助时间>120h
    //MTTA：平均协助时间<15min

    /// <summary>
    /// MaintainDataTable表中"时间戳类型"列的取值
    /// 一段日志的结束，就是下一段日志的开始，任何时刻设备只可能处于其中一种状态
    /// </summary>
    internal enum RecordType
    {
        RunningTime,//设备正常运行
        RepairTime,//设备因机械故障处于维修状态
        AssistanceTime,//设备需要人工干涉的协助状态
    }

    /// <summary>
    /// 日期表的一行映射（MVVM的Model）
    /// </summary>
    public class DateStatisticRow
    {
        public DateTime Date { get; set; }
        public double DayRunningTime { get; set; }
        public double DayAssistanceTime { get; set; }
        public int DayNumberOfRepairs { get; set; }
        public int DayNumberOfAssistance { get; set; }
        public double BeforeRunningTime { get; set; }
        public double BeforeAssistanceTime { get; set; }
        public int BeforeNumberOfRepairs { get; set; }
        public int BeforeNumberOfAssistance { get; set; }
        public double BeforeMTBF { get; set; }
        public double BeforeMTBA { get; set; }
        public double BeforeMTTA { get; set; }
    }

    /// <summary>
    /// 总表的一行映射
    /// </summary>
    public class SummaryRow
    {
        public double TotalRunningTime { get; set; }
        public double TotalAssistanceTime { get; set; }
        public int TotalNumberOfRepairs { get; set; }
        public int TotalNumberOfAssistance { get; set; }
    }

    //维护数据统计数据类（该页面的数据模型类：数据库、表、统计计算全部集中在这里）
    public class MaintainDataStatisticsData
    {
        #region 单例构造
        private MaintainDataStatisticsData() 
        {
        }

        /// <summary>
        /// 静态构造：注册SQLite原生提供程序
        /// 项目引用的是Microsoft.Data.Sqlite.Core（不含自动初始化的bundle包），
        /// 若不显式初始化，SqliteConnection.Open()会抛"You need to call SQLitePCL.raw.SetProvider()"
        /// </summary>
        static MaintainDataStatisticsData()
        {
            
            SQLitePCL.Batteries_V2.Init();
            //注意：不能使用 new Lazy<T>() 的默认构造方式，它要求类型具备"公共无参构造函数"，
            //本类构造函数为私有，必须显式传入工厂委托，否则运行时会抛 MissingMemberException
            Lazy = new Lazy<MaintainDataStatisticsData>(() => new MaintainDataStatisticsData());
            Instent = Lazy.Value;
        }

        private static readonly Lazy<MaintainDataStatisticsData> Lazy;
        public static MaintainDataStatisticsData Instent;
        #endregion

        //维护数据统计数据库地址
        public string DatabsePath = System.AppDomain.CurrentDomain.BaseDirectory;

        public string DatabseName = "MaintainDataStatisticsData.db";

        //时间字符串统一格式，SQLite中没有日期类型，统一用该格式的TEXT保存，可直接按字符串比较/排序
        public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
        //日期主键统一格式（yyyyMMdd整数），范围查询快且天然唯一
        public const string DateKeyFormat = "yyyyMMdd";

        #region 数据处理工具函数
        /// <summary>
        /// 连接命令，Data Source数据库名称，Foreign Keys不存在数据库时，自动创建数据库
        /// </summary>
        string connectionString ;
        public SqliteConnection? connection;

        /// <summary>
        /// 连接数据库。
        /// 数据库构造：
        /// 总表：累计正常运行时间，累计辅助时间，故障维修次数，辅助次数。
        /// 时间戳统计表：时间戳类型（正常运行时间，维修时间，协助处理时间），开始时间，结束时间
        /// 日期统计表:日期，当日正常运行时间，当日辅助时间，当日故障维修次数，当日辅助次数。当日之前累计正常运行时间，当日之前累计辅助时间，当日之前故障维修次数，当日之前辅助次数。当日之前MTBF，MTBA，MTTA
        /// </summary>
        public void ConnectDatabase()
        {
            string path = System.IO.Path.Combine(DatabsePath, DatabseName);
            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                //确保数据库文件所在目录存在（否则SQLite无法创建文件）
                Directory.CreateDirectory(directory);
            }

            connectionString = $"Data Source={path};Foreign Keys=True";
            connection = new SqliteConnection(connectionString);
            connection.Open();

            //批量写入时的性能与稳定性设置（WAL允许读写并发，busy_timeout避免瞬时锁冲突）
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA busy_timeout=3000;");

            //查询表是否存在，不存在则创建表
            if (!TableExists(connection, MaintainDataName.SummaryTable.ToString()))
            {
                //创建总表
                SqliteCommand cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS SummaryTable (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TotalRunningTime REAL,
                        TotalAssistanceTime REAL,
                        TotalNumberOfRepairs INTEGER,
                        TotalNumberOfAssistance INTEGER
                    );";
                //CREATE TABLE IF NOT EXISTS  表不存在时才创建
                //INTEGER      整数
                //REAL      浮点数
                //PRIMARY KEY   主键，同时会创建索引，利于范围查询
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }

            if (!TableExists(connection, MaintainDataName.MaintainDataTable.ToString()))
            {
                //创建时间戳统计表（维护数据表）
                SqliteCommand cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS MaintainDataTable (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TimestampType TEXT NOT NULL,
                        SatrtTime TEXT NOT NULL DEFAULT '',
                        EndTime TEXT NOT NULL DEFAULT ''
                    );";
                //DEFAULT 默认值
                //datetime('now')    当前 UTC 日期时间
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }

            if (!TableExists(connection, MaintainDataName.DateTable.ToString()))
            {
                //创建日期表
                SqliteCommand cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS DateTable (
                        DateTime INTEGER PRIMARY KEY,
                        DayRunningTime REAL,
                        DayAssistanceTime REAL,
                        DayNumberOfRepairs INTEGER,
                        DayNumberOfAssistance INTEGER,
                        BeforeRunningTime REAL,
                        BeforeAssistanceTime REAL,
                        BeforeNumberOfRepairs INTEGER,
                        BeforeNumberOfAssistance INTEGER,
                        BeforeMTBF REAL,
                        BeforeMTBA REAL,
                        BeforeMTTA REAL
                    );";
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }

            //总表只保留唯一一行累计数据
            if (Convert.ToInt64(Scalar($"SELECT COUNT(*) FROM {MaintainDataName.SummaryTable};")) == 0)
            {
                Execute($@"
                    INSERT INTO {MaintainDataName.SummaryTable}
                        ({MaintainDataName.TotalRunningTime},
                         {MaintainDataName.TotalAssistanceTime},
                         {MaintainDataName.TotalNumberOfRepairs},
                         {MaintainDataName.TotalNumberOfAssistance})
                    VALUES (0,0,0,0);");
            }
        }

        //查找是否存在指定名称的表
        static bool TableExists(SqliteConnection connection, string tableName)
        {
            using var cmd = connection.CreateCommand();

            cmd.CommandText = @"
            SELECT EXISTS (     
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                    AND name = $name COLLATE NOCASE
            );";
            //sqlite_master SQLite 系统表，保存表、索引、视图、触发器的定义
            //type = 'table'    只查找表，排除索引、视图等
            //COLLATE NOCASE   表名比较不区分大小写，因为 SQLite 标识符通常不区分大小写
            //SELECT 1    找到任意一条就返回1，不需要返回具体列
            //EXISTS (...)     子查询有结果返回 1，没有返回 0
            cmd.Parameters.AddWithValue("$name", tableName);

            var result = cmd.ExecuteScalar();

            return Convert.ToInt64(result) == 1;
        }

        /// <summary>
        /// 保证数据库可用（幂等），所有对外方法入口都会调用
        /// </summary>
        void EnsureConnection()
        {
            if (connection == null)
            {
                ConnectDatabase();
                return;
            }
            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }
        }

        //执行一条无返回值的SQL
        void Execute(string sql, params (string Name, object? Value)[] parameters)
        {
            EnsureConnection();
            using var cmd = connection!.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters)
            {
                cmd.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value);
            }
            cmd.ExecuteNonQuery();
        }

        //执行一条返回单值的SQL
        object? Scalar(string sql, params (string Name, object? Value)[] parameters)
        {
            EnsureConnection();
            using var cmd = connection!.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters)
            {
                cmd.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value);
            }
            var value = cmd.ExecuteScalar();
            return value == DBNull.Value ? null : value;
        }

        static string FormatTime(DateTime time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);
        static string FormatDateKey(DateTime date) => date.ToString(DateKeyFormat, CultureInfo.InvariantCulture);

        static DateTime? ParseTime(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (text.Length == 0) return null;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                return time;
            }
            //兼容只精确到日的日期字符串
            if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                return time;
            }
            return null;
        }

        static DateTime ParseDateKey(object? value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return DateTime.ParseExact(text, DateKeyFormat, CultureInfo.InvariantCulture);
        }
        #endregion

        #region 维护数据表（时间戳日志）操作
        /// <summary>
        /// 查询时间戳表最近的一条记录，没有记录时返回null
        /// </summary>
        public (long Id, string TimestampType, string SatrtTime, string EndTime)? GetLastRecord()
        {
            EnsureConnection();
            using var cmd = connection!.CreateCommand();
            cmd.CommandText = $@"
                SELECT Id, {MaintainDataName.TimestampType}, {MaintainDataName.SatrtTime}, {MaintainDataName.EndTime}
                FROM {MaintainDataName.MaintainDataTable}
                ORDER BY Id DESC
                LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return (reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3));
        }

        /// <summary>
        /// 是否存在没有结束时间的记录（即设备当前正处于某段状态中）
        /// </summary>
        public bool HasOpenRecord()
        {
            EnsureConnection();
            var value = Scalar($@"
                SELECT COUNT(*)
                FROM {MaintainDataName.MaintainDataTable}
                WHERE {MaintainDataName.EndTime} IS NULL OR {MaintainDataName.EndTime} = '';");
            return Convert.ToInt64(value ?? 0L) > 0;
        }

        /// <summary>
        /// 把当前处于打开状态的记录以给定时间结束掉，并做增量累计。
        /// 遵循"一段日志的结束就是下一段日志的开始"，任何状态切换前都必须先结束上一段。
        /// </summary>
        /// <returns>被结束的记录条数（0或1）</returns>
        public int CloseOpenRecord(DateTime endTime)
        {
            EnsureConnection();
            var last = GetLastRecord();
            if (last == null) return 0;
            if (!string.IsNullOrEmpty(last.Value.EndTime)) return 0;

            var start = ParseTime(last.Value.SatrtTime);
            if (start == null)
            {
                //开始时间异常，仅补结束时间，不参与统计
                Execute($@"
                    UPDATE {MaintainDataName.MaintainDataTable}
                    SET {MaintainDataName.EndTime} = $end
                    WHERE Id = $id;",
                    ("$end", FormatTime(endTime)), ("$id", last.Value.Id));
                return 1;
            }

            //时间不允许倒流
            if (endTime < start.Value) endTime = start.Value;

            Execute($@"
                UPDATE {MaintainDataName.MaintainDataTable}
                SET {MaintainDataName.EndTime} = $end
                WHERE Id = $id;",
                ("$end", FormatTime(endTime)), ("$id", last.Value.Id));

            //累加到总表和日期表
            ApplyInterval(last.Value.TimestampType, start.Value, endTime, +1);
            return 1;
        }

        /// <summary>
        /// 追加一条日志记录（不自动结束上一条，由调用方保证状态切换的正确顺序）
        /// </summary>
        public void AppendRecord(string timestampType, DateTime startTime)
        {
            EnsureConnection();
            Execute($@"
                INSERT INTO {MaintainDataName.MaintainDataTable}
                    ({MaintainDataName.TimestampType}, {MaintainDataName.SatrtTime}, {MaintainDataName.EndTime})
                VALUES ($type, $start, '');",
                ("$type", timestampType), ("$start", FormatTime(startTime)));
        }

        /// <summary>
        /// 结束上一段日志并开启一段新的日志（状态切换的唯一入口）
        /// </summary>
        public void SwitchTo(string timestampType, DateTime time)
        {
            CloseOpenRecord(time);
            AppendRecord(timestampType, time);
        }

        /// <summary>
        /// 把一段时间区间累加进总表与日期表（sign为+1表示累加，-1表示冲销）
        /// 跨天的区间会按自然日拆分，保证日期表按天统计准确
        /// </summary>
        void ApplyInterval(string timestampType, DateTime start, DateTime end, int sign)
        {
            if (end <= start) return;
            double hours = (end - start).TotalHours;

            switch (timestampType)
            {
                case nameof(RecordType.RunningTime):
                    Execute($@"
                        UPDATE {MaintainDataName.SummaryTable}
                        SET {MaintainDataName.TotalRunningTime} = {MaintainDataName.TotalRunningTime} + $hours;",
                        ("$hours", hours * sign));
                    break;
                case nameof(RecordType.AssistanceTime):
                    Execute($@"
                        UPDATE {MaintainDataName.SummaryTable}
                        SET {MaintainDataName.TotalAssistanceTime} = {MaintainDataName.TotalAssistanceTime} + $hours,
                            {MaintainDataName.TotalNumberOfAssistance} = {MaintainDataName.TotalNumberOfAssistance} + $count;",
                        ("$hours", hours * sign), ("$count", sign));
                    break;
                case nameof(RecordType.RepairTime):
                    Execute($@"
                        UPDATE {MaintainDataName.SummaryTable}
                        SET {MaintainDataName.TotalNumberOfRepairs} = {MaintainDataName.TotalNumberOfRepairs} + $count;",
                        ("$count", sign));
                    break;
                default:
                    //未知类型不参与统计
                    return;
            }

            //按自然日拆分
            DateTime cursor = start;
            while (cursor < end)
            {
                DateTime dayEnd = cursor.Date.AddDays(1);
                if (dayEnd > end) dayEnd = end;
                double part = (dayEnd - cursor).TotalHours;
                if (part > 0)
                {
                    AccumulateDay(cursor.Date, timestampType, part, sign);
                }
                cursor = dayEnd;
            }
        }

        /// <summary>
        /// 把某个自然日的增量累加进日期表（不存在该日期则先补一条"当日之前"快照）
        /// </summary>
        void AccumulateDay(DateTime date, string timestampType, double hours, int sign)
        {
            EnsureDateRow(date);
            string key = FormatDateKey(date);
            switch (timestampType)
            {
                case nameof(RecordType.RunningTime):
                    Execute($@"
                        UPDATE {MaintainDataName.DateTable}
                        SET {MaintainDataName.DayRunningTime} = {MaintainDataName.DayRunningTime} + $hours
                        WHERE {MaintainDataName.DateTime} = $key;",
                        ("$hours", hours * sign), ("$key", key));
                    break;
                case nameof(RecordType.AssistanceTime):
                    Execute($@"
                        UPDATE {MaintainDataName.DateTable}
                        SET {MaintainDataName.DayAssistanceTime} = {MaintainDataName.DayAssistanceTime} + $hours,
                            {MaintainDataName.DayNumberOfAssistance} = {MaintainDataName.DayNumberOfAssistance} + $count
                        WHERE {MaintainDataName.DateTime} = $key;",
                        ("$hours", hours * sign), ("$count", sign), ("$key", key));
                    break;
                case nameof(RecordType.RepairTime):
                    Execute($@"
                        UPDATE {MaintainDataName.DateTable}
                        SET {MaintainDataName.DayNumberOfRepairs} = {MaintainDataName.DayNumberOfRepairs} + $count
                        WHERE {MaintainDataName.DateTime} = $key;",
                        ("$count", sign), ("$key", key));
                    break;
            }
        }

        /// <summary>
        /// 日期表不存在该日期时插入一行，"当日之前"四项取前一日（无则取0）的当日值累加结果，
        /// 这样历史数据即使跨天被重新统计也能自洽
        /// </summary>
        void EnsureDateRow(DateTime date)
        {
            string key = FormatDateKey(date);
            if (Convert.ToInt64(Scalar($@"
                    SELECT COUNT(*) FROM {MaintainDataName.DateTable}
                    WHERE {MaintainDataName.DateTime} = $key;",
                    ("$key", key)) ?? 0L) > 0)
            {
                return;
            }

            var before = GetBeforeSnapshot(date);
            Execute($@"
                INSERT INTO {MaintainDataName.DateTable} (
                    {MaintainDataName.DateTime},
                    {MaintainDataName.DayRunningTime}, {MaintainDataName.DayAssistanceTime},
                    {MaintainDataName.DayNumberOfRepairs}, {MaintainDataName.DayNumberOfAssistance},
                    {MaintainDataName.BeforeRunningTime}, {MaintainDataName.BeforeAssistanceTime},
                    {MaintainDataName.BeforeNumberOfRepairs}, {MaintainDataName.BeforeNumberOfAssistance},
                    {MaintainDataName.BeforeMTBF}, {MaintainDataName.BeforeMTBA}, {MaintainDataName.BeforeMTTA})
                VALUES ($key, 0, 0, 0, 0, $br, $ba, $bnr, $bna, $mtbf, $mtba, $mtta);",
                ("$key", key),
                ("$br", before.Running), ("$ba", before.Assistance),
                ("$bnr", before.Repairs), ("$bna", before.AssistanceCount),
                ("$mtbf", before.MTBF), ("$mtba", before.MTBA), ("$mtta", before.MTTA));
        }

        /// <summary>
        /// 计算某个日期"当日之前"的累计快照：优先取前一天的（当日值+当日之前值）累加结果
        /// </summary>
        (double Running, double Assistance, int Repairs, int AssistanceCount, double MTBF, double MTBA, double MTTA) GetBeforeSnapshot(DateTime date)
        {
            string previousKey = FormatDateKey(date.AddDays(-1));
            using var cmd = connection!.CreateCommand();
            cmd.CommandText = $@"
                SELECT {MaintainDataName.DayRunningTime}, {MaintainDataName.DayAssistanceTime},
                       {MaintainDataName.DayNumberOfRepairs}, {MaintainDataName.DayNumberOfAssistance},
                       {MaintainDataName.BeforeRunningTime}, {MaintainDataName.BeforeAssistanceTime},
                       {MaintainDataName.BeforeNumberOfRepairs}, {MaintainDataName.BeforeNumberOfAssistance}
                FROM {MaintainDataName.DateTable}
                WHERE {MaintainDataName.DateTime} = $key;";
            cmd.Parameters.AddWithValue("$key", previousKey);

            double running = 0, assistance = 0;
            int repairs = 0, assistanceCount = 0;
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    running = (reader.IsDBNull(0) ? 0 : reader.GetDouble(0)) + (reader.IsDBNull(4) ? 0 : reader.GetDouble(4));
                    assistance = (reader.IsDBNull(1) ? 0 : reader.GetDouble(1)) + (reader.IsDBNull(5) ? 0 : reader.GetDouble(5));
                    repairs = (reader.IsDBNull(2) ? 0 : reader.GetInt32(2)) + (reader.IsDBNull(6) ? 0 : reader.GetInt32(6));
                    assistanceCount = (reader.IsDBNull(3) ? 0 : reader.GetInt32(3)) + (reader.IsDBNull(7) ? 0 : reader.GetInt32(7));
                }
            }
            return (running, assistance, repairs, assistanceCount,
                    Mtbf(running, repairs), Mtba(running, assistanceCount), Mtta(assistance, assistanceCount));
        }

        /// <summary>
        /// 用时间戳表的全部记录重算总表与日期表（导出、修复、历史数据导入后使用）
        /// </summary>
        public void RebuildStatistic()
        {
            EnsureConnection();
            Execute($"DELETE FROM {MaintainDataName.SummaryTable};");
            Execute($@"
                INSERT INTO {MaintainDataName.SummaryTable}
                    ({MaintainDataName.TotalRunningTime}, {MaintainDataName.TotalAssistanceTime},
                     {MaintainDataName.TotalNumberOfRepairs}, {MaintainDataName.TotalNumberOfAssistance})
                VALUES (0,0,0,0);");
            Execute($"DELETE FROM {MaintainDataName.DateTable};");

            foreach (var record in GetRecords(null, null))
            {
                var start = ParseTime(record.SatrtTime);
                var end = ParseTime(record.EndTime);
                if (start == null || end == null) continue;
                ApplyInterval(record.TimestampType, start.Value, end.Value, +1);
            }
        }
        #endregion

        #region 查询接口
        /// <summary>
        /// 读取时间戳表记录，时间范围为空表示不限制（只统计有结束时间的完整区间）
        /// </summary>
        public List<(long Id, string TimestampType, string SatrtTime, string EndTime)> GetRecords(DateTime? from, DateTime? to)
        {
            EnsureConnection();
            var list = new List<(long, string, string, string)>();
            using var cmd = connection!.CreateCommand();
            var sql = new StringBuilder($@"
                SELECT Id, {MaintainDataName.TimestampType}, {MaintainDataName.SatrtTime}, {MaintainDataName.EndTime}
                FROM {MaintainDataName.MaintainDataTable}
                WHERE 1 = 1");
            if (from.HasValue)
            {
                sql.Append($" AND {MaintainDataName.SatrtTime} >= $from");
                cmd.Parameters.AddWithValue("$from", FormatTime(from.Value));
            }
            if (to.HasValue)
            {
                sql.Append($" AND {MaintainDataName.SatrtTime} <= $to");
                cmd.Parameters.AddWithValue("$to", FormatTime(to.Value));
            }
            sql.Append(" ORDER BY Id ASC;");
            cmd.CommandText = sql.ToString();

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add((reader.GetInt64(0),
                          reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                          reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                          reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
            }
            return list;
        }

        /// <summary>
        /// 总表累计数据
        /// </summary>
        public SummaryRow GetSummary()
        {
            EnsureConnection();
            var row = new SummaryRow();
            using var cmd = connection!.CreateCommand();
            cmd.CommandText = $@"
                SELECT {MaintainDataName.TotalRunningTime}, {MaintainDataName.TotalAssistanceTime},
                       {MaintainDataName.TotalNumberOfRepairs}, {MaintainDataName.TotalNumberOfAssistance}
                FROM {MaintainDataName.SummaryTable}
                ORDER BY Id ASC
                LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                row.TotalRunningTime = reader.IsDBNull(0) ? 0 : reader.GetDouble(0);
                row.TotalAssistanceTime = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
                row.TotalNumberOfRepairs = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                row.TotalNumberOfAssistance = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            }
            return row;
        }

        /// <summary>
        /// 读取日期表指定区间（含首尾）的数据，无记录的日期直接跳过
        /// </summary>
        public List<DateStatisticRow> GetDailyStatistics(DateTime start, DateTime end)
        {
            EnsureConnection();
            var list = new List<DateStatisticRow>();
            using var cmd = connection!.CreateCommand();
            cmd.CommandText = $@"
                SELECT {MaintainDataName.DateTime},
                       {MaintainDataName.DayRunningTime}, {MaintainDataName.DayAssistanceTime},
                       {MaintainDataName.DayNumberOfRepairs}, {MaintainDataName.DayNumberOfAssistance},
                       {MaintainDataName.BeforeRunningTime}, {MaintainDataName.BeforeAssistanceTime},
                       {MaintainDataName.BeforeNumberOfRepairs}, {MaintainDataName.BeforeNumberOfAssistance},
                       {MaintainDataName.BeforeMTBF}, {MaintainDataName.BeforeMTBA}, {MaintainDataName.BeforeMTTA}
                FROM {MaintainDataName.DateTable}
                WHERE {MaintainDataName.DateTime} >= $from AND {MaintainDataName.DateTime} <= $to
                ORDER BY {MaintainDataName.DateTime} ASC;";
            cmd.Parameters.AddWithValue("$from", FormatDateKey(start.Date));
            cmd.Parameters.AddWithValue("$to", FormatDateKey(end.Date));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new DateStatisticRow
                {
                    Date = ParseDateKey(reader.GetValue(0)),
                    DayRunningTime = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
                    DayAssistanceTime = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                    DayNumberOfRepairs = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    DayNumberOfAssistance = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    BeforeRunningTime = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
                    BeforeAssistanceTime = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                    BeforeNumberOfRepairs = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    BeforeNumberOfAssistance = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    BeforeMTBF = reader.IsDBNull(9) ? 0 : reader.GetDouble(9),
                    BeforeMTBA = reader.IsDBNull(10) ? 0 : reader.GetDouble(10),
                    BeforeMTTA = reader.IsDBNull(11) ? 0 : reader.GetDouble(11),
                });
            }
            return list;
        }

        /// <summary>
        /// 读取指定日期的"当日之前"累计快照
        /// </summary>
        public DateStatisticRow? GetBeforeOfDay(DateTime date)
        {
            EnsureConnection();
            using var cmd = connection!.CreateCommand();
            cmd.CommandText = $@"
                SELECT {MaintainDataName.BeforeRunningTime}, {MaintainDataName.BeforeAssistanceTime},
                       {MaintainDataName.BeforeNumberOfRepairs}, {MaintainDataName.BeforeNumberOfAssistance},
                       {MaintainDataName.BeforeMTBF}, {MaintainDataName.BeforeMTBA}, {MaintainDataName.BeforeMTTA}
                FROM {MaintainDataName.DateTable}
                WHERE {MaintainDataName.DateTime} = $key
                LIMIT 1;";
            cmd.Parameters.AddWithValue("$key", FormatDateKey(date.Date));
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return new DateStatisticRow
            {
                Date = date.Date,
                BeforeRunningTime = reader.IsDBNull(0) ? 0 : reader.GetDouble(0),
                BeforeAssistanceTime = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
                BeforeNumberOfRepairs = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                BeforeNumberOfAssistance = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                BeforeMTBF = reader.IsDBNull(4) ? 0 : reader.GetDouble(4),
                BeforeMTBA = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
                BeforeMTTA = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
            };
        }
        #endregion

        #region 指标与展示计算
        /// <summary>
        /// MTBF：平均故障间隔时间 = 累计正常运行时间 / 故障维修次数，单位小时，无数据返回0
        /// </summary>
        public static double Mtbf(double runningHours, int repairCount)
            => repairCount > 0 ? runningHours / repairCount : 0d;

        /// <summary>
        /// MTBA：平均间隔辅助时间 = 累计正常运行时间 / 协助次数，单位小时，无数据返回0
        /// </summary>
        public static double Mtba(double runningHours, int assistanceCount)
            => assistanceCount > 0 ? runningHours / assistanceCount : 0d;

        /// <summary>
        /// MTTA：平均协助时间 = 累计协助时间 / 协助次数，此处按需求折算为分钟，无数据返回0
        /// </summary>
        public static double Mtta(double assistanceHours, int assistanceCount)
            => assistanceCount > 0 ? assistanceHours * 60d / assistanceCount : 0d;

        /// <summary>
        /// 某一天的MTBF（小时）：当日正常运行时间 / 当日故障维修次数
        /// </summary>
        public static double DayMtbf(DateStatisticRow row)
            => row.DayNumberOfRepairs > 0 ? row.DayRunningTime / row.DayNumberOfRepairs : 0d;

        /// <summary>
        /// 某一天的MTBA（小时）：当日正常运行时间 / 当日协助次数
        /// </summary>
        public static double DayMtba(DateStatisticRow row)
            => row.DayNumberOfAssistance > 0 ? row.DayRunningTime / row.DayNumberOfAssistance : 0d;

        /// <summary>
        /// 某一天的MTTA（分钟）：当日协助时间 / 当日协助次数
        /// </summary>
        public static double DayMtta(DateStatisticRow row)
            => row.DayNumberOfAssistance > 0 ? row.DayAssistanceTime * 60d / row.DayNumberOfAssistance : 0d;

        /// <summary>
        /// 把小时数格式化为"x小时y分"的可读文本
        /// </summary>
        public static string FormatHours(double hours)
        {
            if (hours <= 0) return "0小时";
            long totalMinutes = (long)Math.Round(hours * 60d);
            long h = totalMinutes / 60;
            long m = totalMinutes % 60;
            return m == 0 ? $"{h}小时" : $"{h}小时{m}分";
        }

        /// <summary>
        /// 清空全部统计数据（调试/重新开始统计时使用）
        /// </summary>
        public void ClearAll()
        {
            EnsureConnection();
            Execute($"DELETE FROM {MaintainDataName.MaintainDataTable};");
            Execute($"DELETE FROM {MaintainDataName.DateTable};");
            Execute($"DELETE FROM {MaintainDataName.SummaryTable};");
            Execute($@"
                INSERT INTO {MaintainDataName.SummaryTable}
                    ({MaintainDataName.TotalRunningTime}, {MaintainDataName.TotalAssistanceTime},
                     {MaintainDataName.TotalNumberOfRepairs}, {MaintainDataName.TotalNumberOfAssistance})
                VALUES (0,0,0,0);");
        }

        /// <summary>
        /// 演示用：生成一段跨越若干天的样例日志（仅用于界面演示，正常运行时不要调用）
        /// </summary>
        /// <param name="days">生成的自然日天数</param>
        /// <param name="leaveOpenTail">是否在末尾保留一条未闭合的运行日志（模拟设备此刻正在运行）</param>
        public void SeedDemoData(int days = 10, bool leaveOpenTail = true)
        {
            EnsureConnection();
            ClearAll();
            var random = new Random(20240521);
            DateTime firstDay = DateTime.Today.AddDays(-(days - 1));
            DateTime lastCursor = firstDay.AddHours(8);

            for (int day = 0; day < days; day++)
            {
                //每天从 08:00 开始，保证数据均匀落在各个自然日上；
                //用"基准日期 + 天数偏移"推进，不能直接用上一段结束时间推进，否则会漏掉自然日
                DateTime cursor = firstDay.AddDays(day).AddHours(8);

                //每天：正常运行 -> 1次维修 -> 正常运行 -> 1次协助
                AppendRecord(nameof(RecordType.RunningTime), cursor);
                cursor = cursor.AddMinutes(300 + random.Next(0, 240));
                CloseOpenRecord(cursor);

                AppendRecord(nameof(RecordType.RepairTime), cursor);
                cursor = cursor.AddMinutes(20 + random.Next(0, 100));
                CloseOpenRecord(cursor);

                AppendRecord(nameof(RecordType.RunningTime), cursor);
                cursor = cursor.AddMinutes(200 + random.Next(0, 200));
                CloseOpenRecord(cursor);

                AppendRecord(nameof(RecordType.AssistanceTime), cursor);
                cursor = cursor.AddMinutes(5 + random.Next(0, 25));
                CloseOpenRecord(cursor);

                lastCursor = cursor;
            }

            if (leaveOpenTail)
            {
                //最后补一条正常运行日志，使设备处于"运行中"，便于演示维护窗口的按钮状态
                AppendRecord(nameof(RecordType.RunningTime), lastCursor);
            }
        }

       
        #endregion
    }
    /// <summary>
    /// bool -> Brush 的转换器，用于指标是否达标的前景色
    /// </summary>
    public class BoolToBrushConverter : System.Windows.Data.IValueConverter
    {
        public Brush TrueBrush { get; set; } = Brushes.SeaGreen;
        public Brush FalseBrush { get; set; } = Brushes.IndianRed;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? TrueBrush : FalseBrush;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
    /// <summary>
    /// 命令绑定相关类，点击命令
    /// </summary>
    public class ClickICommand<T> : ICommand
    {
        public event EventHandler CanExecuteChanged;

        public Action<T> action;

        public ClickICommand(Action<T> _Action)
        {
            action = _Action;
        }
        public bool CanExecute(object param)
        {
            return true;
        }

        public void Execute(object param)
        {
            action?.Invoke((T)param);
        }
    }
    public class ClickICommand : ICommand
    {
        public event EventHandler CanExecuteChanged;

        public Action action;

        public ClickICommand(Action _Action)
        {
            action = _Action;
        }
        public bool CanExecute(object param)
        {
            return true;
        }

        public void Execute(object param)
        {
            action?.Invoke();
        }
    }
}
