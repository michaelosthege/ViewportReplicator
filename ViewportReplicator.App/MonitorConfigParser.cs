using System;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;

namespace ViewportReplicator.App
{
    internal class MonitorConfigParser
    {
        public static Rectangle? GetViewportRegion(string viewportName, string configPath)
        {
            configPath = Environment.ExpandEnvironmentVariables(configPath);
            if (!File.Exists(configPath))
                return null;
            string configText = File.ReadAllText(configPath);
            string definitionPattern = viewportName + ".*?=.*?(\\{.*?\\})";
            string sizesPattern = "x = (?<x>\\d+?), y = (?<y>\\d+?), width = (?<width>\\d+?), height = (?<height>\\d+?) ";
            Match viewportDefinition = Regex.Match(configText, definitionPattern);
            if (!viewportDefinition.Success)
                return null;
            Match sizes = Regex.Match(viewportDefinition.Value, sizesPattern);
            if (!sizes.Success)
                return null;
            return new Rectangle(
                x: Convert.ToInt32(sizes.Groups["x"].Value),
                y: Convert.ToInt32(sizes.Groups["y"].Value),
                width: Convert.ToInt32(sizes.Groups["width"].Value),
                height: Convert.ToInt32(sizes.Groups["height"].Value)
            );
        }
    }
}
