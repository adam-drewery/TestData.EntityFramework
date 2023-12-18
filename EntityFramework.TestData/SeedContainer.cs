using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EntityFramework.TestData;

public class SeedContainer(DbContext dbContext)
{
    private readonly Dictionary<Type, Seed> _seeds = new();
    private readonly Dictionary<Type, object[]> _existingItems = new();
    private readonly Dictionary<Type, object> _dependencies = new();

    public void LoadDependency<T>(T dependency)
    {
        _dependencies.Add(typeof(T), dependency ?? throw new ArgumentNullException(nameof(dependency)));
    }
    
    public void LoadSeeds(Assembly assembly)
    {
        var seedTypes = assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(Seed)))
            .Where(t => !t.IsAbstract)
            .ToList();
        
        var allDependencies = seedTypes.SelectMany(t => t.GetInterfaces()
            .Where(i => i.IsGenericType)
            .Where(i => i.GetGenericTypeDefinition() == typeof(IDependsOn<>))
            .Select(i => i.GetGenericArguments()[0]))
            .Distinct()
            .ToList();
        
        var targetTypes = seedTypes.SelectMany(t => t.GetInterfaces()
            .Where(i => i.IsGenericType)
            .Where(i => i.GetGenericTypeDefinition() == typeof(ISeed<>))
            .Select(i => i.GetGenericArguments()[0]))
            .Distinct()
            .ToList();

        
        foreach (var dependency in allDependencies.Concat(targetTypes).Distinct())
        {
            var method = typeof(SeedContainer).GetMethod(nameof(LoadFromDatabase), BindingFlags.NonPublic | BindingFlags.Instance);
            var genericMethod = method!.MakeGenericMethod(dependency);
            var existingItems = (object[])genericMethod.Invoke(this, null)!;
            
            if (existingItems.Length > 0)
                _existingItems[dependency] = existingItems;
        }
        
        while(seedTypes.Count != 0)
        {
            var typesToRemove = new List<Type>();
            foreach (var seedType in seedTypes)
            {
                var dependencies = seedType.GetInterfaces()
                    .Where(i => i.IsGenericType)
                    .Where(i => i.GetGenericTypeDefinition() == typeof(IDependsOn<>))
                    .Select(i => i.GetGenericArguments()[0])
                    .ToList();

                // skip if we have unprocessed dependencies- not all of them are available yet
                if (!dependencies.All(d => _seeds.ContainsKey(d) || _existingItems.ContainsKey(d))) continue;
                
                var seed = (Seed?)Activator.CreateInstance(seedType);
                    
                if (seed == null) 
                    throw new InvalidOperationException($"Could not create instance of {seedType.Name}.");
                
                // add other seeds as dependencies
                var seedDependencies = dependencies.Where(d => !_existingItems.ContainsKey(d)).Select(d => _seeds[d]);
                seed.AddDependencies(seedDependencies);
                
                seed.AddDependencies(_existingItems);

                foreach (var namedSeed in _dependencies)
                    seed.AddDependency(namedSeed.Key, namedSeed.Value);

                _seeds.Add(seed.EntityType, seed);
                typesToRemove.Add(seedType);
            }
            
            if (typesToRemove.Count == 0)
                throw new InvalidOperationException(
                    "Circular dependency detected between the following types: " 
                    + string.Join(", ", seedTypes.Select(t => t.Name)));

            foreach (var type in typesToRemove)
                seedTypes.Remove(type);
            
            typesToRemove.Clear();
        }
    }

    private object[] LoadFromDatabase<T>() where T : class
    {
        return dbContext.Set<T>().Cast<object>().ToArray();
    }
    
    public IEnumerable<IGrouping<Type, object>> Generate()
    {
        var objects = _seeds.SelectMany(s => s.Value.CreateObjects());
        return objects.GroupBy(o => o.GetType());
    }

    public Seed Entity<T>()
    {
        return _seeds[typeof(T)];
    }
}