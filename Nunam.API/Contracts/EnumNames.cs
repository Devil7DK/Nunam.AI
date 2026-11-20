namespace Nunam.API.Contracts
{
    public static class EnumNames
    {
        /// <summary>
        /// Parses <paramref name="value"/> as one of <typeparamref name="T"/>'s names, ignoring case.
        /// Unlike <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/> it refuses numbers, which
        /// would otherwise turn "7" into a value no code path expects.
        /// </summary>
        public static bool TryParse<T>(string? value, out T result) where T : struct, Enum
        {
            var name = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            result = name is null ? default : Enum.Parse<T>(name);
            return name is not null;
        }
    }
}
