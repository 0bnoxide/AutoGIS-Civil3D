using AutoGIS.Civil3D.FixtureBuilder;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: AutoGIS.Civil3D.FixtureBuilder <output-directory>");
    return 2;
}

FixtureCatalog.WriteAll(args[0]);
return 0;
