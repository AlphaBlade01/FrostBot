using System.Reflection;
using System.Text;

namespace FrostBot.Logic.Library;

public class PageHelper
{
    public static string FlattenPropertiesIntoString(object container, string[] ignoredProperties)
    {
        var builder = new StringBuilder();
        PropertyInfo[] properties = container.GetType().GetProperties();

        foreach (PropertyInfo property in properties)
        {
            if (ignoredProperties.Contains(property.Name)) continue;
            builder.AppendLine($"`{property.Name}`: {property.GetValue(container)}");
        }

        return builder.ToString();
    }

    public static string FlattenEnumerableIntoString<T>(IEnumerable<T> container, Func<T, string> propertyFlattener)
    {
        var builder = new StringBuilder();
        foreach (T item in container)
        {
            builder.AppendLine(propertyFlattener.Invoke(item));
        }
        return builder.ToString();
    }
}
