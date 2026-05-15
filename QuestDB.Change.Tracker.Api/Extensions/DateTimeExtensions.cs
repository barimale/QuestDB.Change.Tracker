using System;

namespace QuestDB.Change.Tracker.Api.Extensions
{
    public static class DateTimeExtensions
    {
        public static DateTime SpecifyUtc(this DateTime dt)
            => DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }
}