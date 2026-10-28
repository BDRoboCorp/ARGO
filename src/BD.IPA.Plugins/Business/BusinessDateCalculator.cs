using System;

namespace BD.IPA.Plugins.Business
{
    /// <summary>
    /// Provides calculations for dates that exclude weekends and configured holidays.
    /// </summary>
    internal static class BusinessDateCalculator
    {
        public static DateTime MoveToBusinessDay(DateTime date)
        {
            while (IsWeekend(date))
            {
                date = date.AddDays(1);
            }

            return date;
        }

        public static DateTime AddBusinessDays(DateTime date, int days)
        {
            if (days < 0) throw new ArgumentOutOfRangeException(nameof(days));

            date = MoveToBusinessDay(date);
            while (days > 0)
            {
                date = MoveToBusinessDay(date.AddDays(1));
                days--;
            }

            return date;
        }

        /// <summary>
        /// Counts weekdays including both endpoints, ignoring their time of day.
        /// </summary>
        public static int CountBusinessDays(DateTime startDate, DateTime endDate)
        {
            if (endDate.Date < startDate.Date) throw new ArgumentException("Plan End Date must not be before Plan Start Date.");

            var days = 0;
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                if (!IsWeekend(date)) days++;
            }

            return days;
        }

        private static bool IsWeekend(DateTime date)
        {
            return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
        }
    }
}
