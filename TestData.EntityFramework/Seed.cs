namespace TestData.EntityFramework;

public abstract class Seed
{
    private readonly Dictionary<Type, Seed> _dependencies = new();
    private readonly Dictionary<Type, object> _singletonDependencies = new();
    
    protected Dictionary<Type, object[]>? ExistingItems { get; private set; }

    public virtual int Count { get; set; } = 20;

    protected static Random Random { get; } = new();

    protected abstract object SingleObject();

    public abstract Type EntityType { get; }

    public abstract Type[] DependencyTypes { get; }

    public IEnumerable<T> Dependencies<T>() where T : class
    {
        if (ExistingItems?.TryGetValue(typeof(T), out var items) ?? false)
            return items.Cast<T>();

        if (!DependencyTypes.Contains(typeof(T)))
            throw new InvalidOperationException("Access attempted to type that isn't marked as a dependency: " + typeof(T).Name);
        
        if (_dependencies.TryGetValue(typeof(T), out var result))
            return result.Items.Cast<T>();

        throw new InvalidDataException("No items of type " + typeof(T).Name + " found.");
    }

    public T Dependency<T>() => (T)_singletonDependencies[typeof(T)];

    internal void AddDependencies(IEnumerable<Seed> dependencies)
    {
        foreach (var dependency in dependencies)
            _dependencies[dependency.EntityType] = dependency;
    }

    internal void AddDependency(Type name, object dependency)
    {
        _singletonDependencies[name] = dependency;
    }

    public abstract IList<object> Items { get; }

    public abstract IEnumerable<object> CreateObjects();

    public void AddDependencies(Dictionary<Type, object[]> existingItems)
    {
        ExistingItems = existingItems;
    }
}

public abstract class Seed<T> : Seed, ISeed<T> where T : class
{
    public Seed()
    {
        DependencyTypes = GetType().GetInterfaces()
            .Where(i => i.IsGenericType)
            .Where(i => i.GetGenericTypeDefinition() == typeof(IDependsOn<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToArray();
    }
    public override IList<object> Items { get; } = new List<object>();

    public override IEnumerable<object> CreateObjects() => Create();

    public virtual IEnumerable<T> Create()
    {
        if (ExistingItems?.ContainsKey(EntityType) ?? false)
        {
            if (ExistingItems.Count + Items.Count >= Count)
                yield break;
            
            Count -= ExistingItems.Count + Items.Count;

            if (Count > 0) Count = 0;
        }
        
        // don't regenerate if we already have items
        if (Items.Any())
            foreach (T item in Items)
                yield return item;

        for (var i = 0; i < Count; i++)
        {
            var item = Single();
            Items.Add(item);
            yield return item;
        }
    }

    protected abstract T Single();

    protected override object SingleObject() => Single() ?? throw new InvalidOperationException("Seed returned null.");

    public override Type EntityType => typeof(T);

    public override Type[] DependencyTypes { get; }
}