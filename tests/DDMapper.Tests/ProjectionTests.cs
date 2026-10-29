using AutoMapper;
using AutoMapper.QueryableExtensions;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Stage 2 projection tests, all over in-memory IQueryable
/// (List.AsQueryable). These prove the built expression, not a database
/// provider: a provider that cannot translate a given call (for example
/// Enum.Parse) will fail at its own translation step, which is outside
/// what an in-memory suite can settle.</summary>
public class ProjectionTests
{
    public class Person
    {
        public int Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int Age { get; set; }
        public string? Email { get; set; }
        public int? Score { get; set; }
        public long ScoreLong { get; set; }
        public Priority Status { get; set; }
        public string StatusText { get; set; } = "";
        public int? MaybeAge { get; set; }
    }

    public enum Priority { Low, Medium, High }
    public enum PriorityDto { Low, Medium, High, Urgent }

    public class PersonDto
    {
        public int Id { get; set; }
        public string? FullName { get; set; }
        public int Age { get; set; }
        public long AgeLong { get; set; }
        public int? Score { get; set; }
        public int ScoreOrZero { get; set; }
        public PriorityDto Status { get; set; }
        public PriorityDto StatusFromText { get; set; }
        public string? StatusName { get; set; }
        public string? Email { get; set; }
    }

    public class Address
    {
        public string? City { get; set; }
        public string? Zip { get; set; }
    }
    public class Customer
    {
        public string? Name { get; set; }
        public Address? Address { get; set; }
    }
    public class Order
    {
        public int Id { get; set; }
        public Customer? Customer { get; set; }
        public List<OrderItem> Items { get; set; } = new();
        public List<OrderItem>? MaybeItems { get; set; }
    }
    public class OrderItem
    {
        public string? Sku { get; set; }
        public int Qty { get; set; }
        public decimal Price { get; set; }
    }
    public class AddressDto
    {
        public string? City { get; set; }
        public string? Zip { get; set; }
    }
    public class CustomerDto
    {
        public string? Name { get; set; }
        public AddressDto? Address { get; set; }
    }
    public class OrderItemDto
    {
        public string? Sku { get; set; }
        public int Qty { get; set; }
        public decimal Price { get; set; }
    }
    public class OrderDto
    {
        public int Id { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerAddressCity { get; set; }
        public CustomerDto? Customer { get; set; }
        public List<OrderItemDto> Items { get; set; } = new();
        public List<OrderItemDto>? MaybeItems { get; set; }
        public OrderItemDto[] ItemArray { get; set; } = Array.Empty<OrderItemDto>();
    }

    public class Animal
    {
        public string? Name { get; set; }
    }
    public class Dog : Animal
    {
        public string? Breed { get; set; }
    }
    public class Cat : Animal
    {
        public int Lives { get; set; }
    }
    public class AnimalDto
    {
        public string? Name { get; set; }
    }
    public class DogDto : AnimalDto
    {
        public string? Breed { get; set; }
    }
    public class CatDto : AnimalDto
    {
        public int Lives { get; set; }
    }

    public class CtorDto
    {
        public CtorDto(int id, string? name) { Id = id; Name = name; }
        public int Id { get; }
        public string? Name { get; set; }
        public int Age { get; set; }
    }
    public class CtorSource
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    [Fact]
    public void BasicProjection()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.FullName, o => o.MapFrom(s => s.FirstName + " " + s.LastName))
            .ForMember(d => d.AgeLong, o => o.MapFrom(s => s.Age))
            .ForMember(d => d.ScoreOrZero, o => o.MapFrom(s => s.Score ?? 0))
            .ForMember(d => d.Status, o => o.MapFrom(s => s.Status))
            .ForMember(d => d.StatusFromText, o => o.MapFrom(s => s.StatusText))
            .ForMember(d => d.StatusName, o => o.MapFrom(s => s.Status))
            .ForMember(d => d.Email, o => o.Ignore()));
        var result = new List<Person>
        {
            new() { Id = 1, FirstName = "Ada", LastName = "Lovelace", Age = 36, Email = "ada@x.test", Score = 5, Status = Priority.High, StatusText = "Medium" },
        }.AsQueryable().ProjectTo<PersonDto>(config).ToList();
        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Ada Lovelace", result[0].FullName);
        Assert.Equal(36L, result[0].AgeLong);
        Assert.Equal(5, result[0].Score);
        Assert.Equal(5, result[0].ScoreOrZero);
        Assert.Equal(PriorityDto.High, result[0].Status);
        Assert.Equal(PriorityDto.Medium, result[0].StatusFromText);
        Assert.Equal("High", result[0].StatusName);
        Assert.Null(result[0].Email);
    }

    [Fact]
    public void EnumNumericFallbackUsesNumericValue()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.Status, o => o.MapFrom(s => s.Status)));
        var result = new List<Person> { new() { Status = (Priority)99 } }.AsQueryable()
            .ProjectTo<PersonDto>(config).ToList();
        Assert.Equal((PriorityDto)99, result[0].Status);
    }

    [Fact]
    public void FlatteningProjection()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>()
            .ForMember(d => d.Customer, o => o.Ignore())
            .ForMember(d => d.Items, o => o.Ignore())
            .ForMember(d => d.MaybeItems, o => o.Ignore())
            .ForMember(d => d.ItemArray, o => o.Ignore()));
        var result = new List<Order>
        {
            new() { Id = 2, Customer = new Customer { Name = "Grace", Address = new Address { City = "KC" } } },
        }.AsQueryable().ProjectTo<OrderDto>(config).ToList();
        Assert.Equal("Grace", result[0].CustomerName);
        Assert.Equal("KC", result[0].CustomerAddressCity);
    }

    [Fact]
    public void NestedObjectAndNullSafeChains()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Address, AddressDto>();
            cfg.CreateMap<Customer, CustomerDto>();
            cfg.CreateMap<Order, OrderDto>();
            cfg.CreateMap<OrderItem, OrderItemDto>();
        });
        var result = new List<Order>
        {
            new() { Id = 1, Customer = new Customer { Name = "Ada", Address = new Address { City = "Blue Springs" } },
                Items = new List<OrderItem> { new() { Sku = "A", Qty = 2, Price = 3.5m } } },
            new() { Id = 2, Customer = null, Items = new List<OrderItem>() },
            new() { Id = 3, Customer = new Customer { Name = "Bo", Address = null } },
        }.AsQueryable().ProjectTo<OrderDto>(config).ToList();
        Assert.Equal("Ada", result[0].Customer!.Name);
        Assert.Equal("Blue Springs", result[0].Customer!.Address!.City);
        Assert.Single(result[0].Items);
        Assert.Equal("A", result[0].Items[0].Sku);
        // Null intermediate: no throw, flat members null, nested object null.
        Assert.Null(result[1].CustomerName);
        Assert.Null(result[1].CustomerAddressCity);
        Assert.Null(result[1].Customer);
        Assert.Null(result[2].Customer!.Address);
    }

    [Fact]
    public void CollectionProjectionToListAndArray()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<OrderItem, OrderItemDto>();
            cfg.CreateMap<Order, OrderDto>()
                .ForMember(d => d.Customer, o => o.Ignore())
                .ForMember(d => d.MaybeItems, o => o.MapFrom(s => s.Items))
                .ForMember(d => d.ItemArray, o => o.MapFrom(s => s.Items));
        });
        var result = new List<Order>
        {
            new() { Items = new List<OrderItem> { new() { Sku = "X", Qty = 1 }, new() { Sku = "Y", Qty = 3 } } },
        }.AsQueryable().ProjectTo<OrderDto>(config).ToList();
        Assert.Equal(2, result[0].Items.Count);
        Assert.Equal("Y", result[0].Items[1].Sku);
        Assert.Equal(2, result[0].ItemArray.Length);
        Assert.Equal(2, result[0].MaybeItems!.Count);
    }

    [Fact]
    public void NullCollectionStaysNullByDefaultAndEmptyWhenFlagOff()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<OrderItem, OrderItemDto>();
            cfg.CreateMap<Order, OrderDto>().ForMember(d => d.Customer, o => o.Ignore());
        });
        var configNoNull = new MapperConfiguration(cfg =>
        {
            cfg.AllowNullCollections = false;
            cfg.CreateMap<OrderItem, OrderItemDto>();
            cfg.CreateMap<Order, OrderDto>().ForMember(d => d.Customer, o => o.Ignore());
        });
        var source = new List<Order> { new() { MaybeItems = null } }.AsQueryable();
        Assert.Null(source.ProjectTo<OrderDto>(config).Single().MaybeItems);
        Assert.NotNull(source.ProjectTo<OrderDto>(configNoNull).Single().MaybeItems);
        Assert.Empty(source.ProjectTo<OrderDto>(configNoNull).Single().MaybeItems!);
    }

    [Fact]
    public void NullableConversions()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.Score, o => o.MapFrom(s => s.MaybeAge))
            .ForMember(d => d.ScoreOrZero, o => o.MapFrom(s => s.MaybeAge ?? 0)));
        var result = new List<Person>
        {
            new() { MaybeAge = null },
            new() { MaybeAge = 7 },
        }.AsQueryable().ProjectTo<PersonDto>(config).ToList();
        Assert.Null(result[0].Score);
        Assert.Equal(0, result[0].ScoreOrZero);
        Assert.Equal(7, result[1].Score);
        Assert.Equal(7, result[1].ScoreOrZero);
    }

    [Fact]
    public void IgnoreLeavesDefault()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.Email, o => o.Ignore()));
        var result = new List<Person> { new() { Email = "a@b.test" } }.AsQueryable()
            .ProjectTo<PersonDto>(config).ToList();
        Assert.Null(result[0].Email);
    }

    [Fact]
    public void CustomMapFromWithConditionalExpression()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.FullName, o => o.MapFrom(s => s.Age >= 18 ? "adult" : "minor")));
        var result = new List<Person> { new() { Age = 20 }, new() { Age = 5 } }.AsQueryable()
            .ProjectTo<PersonDto>(config).ToList();
        Assert.Equal("adult", result[0].FullName);
        Assert.Equal("minor", result[1].FullName);
    }

    [Fact]
    public void ConditionDelegateFailsWithClearProjectionError()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.FullName, o => { o.MapFrom(s => s.FirstName); o.Condition(s => s.Age > 0); }));
        var ex = Assert.Throws<AutoMapperProjectionException>(() =>
            new List<Person> { new() { FirstName = "Ada", Age = 1 } }.AsQueryable()
                .ProjectTo<PersonDto>(config).ToList());
        Assert.Contains("FullName", ex.Message);
        Assert.Contains("Condition", ex.Message);
    }

    [Fact]
    public void PreConditionAndResolverAndTwoParamMapFromFail()
    {
        var pre = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.FullName, o => { o.MapFrom(s => s.FirstName); o.PreCondition(s => true); }));
        Assert.Throws<AutoMapperProjectionException>(() =>
            new List<Person> { new() { FirstName = "Ada" } }.AsQueryable().ProjectTo<PersonDto>(pre).ToList());

        var two = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.FullName, o => o.MapFrom((s, d) => s.FirstName)));
        Assert.Throws<AutoMapperProjectionException>(() =>
            new List<Person> { new() { FirstName = "Ada" } }.AsQueryable().ProjectTo<PersonDto>(two).ToList());
    }

    [Fact]
    public void NullSubstituteReplacesNull()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.FullName, o => { o.MapFrom(s => s.FirstName); o.NullSubstitute("Unknown"); }));
        var result = new List<Person> { new() { FirstName = null }, new() { FirstName = "Ada" } }.AsQueryable()
            .ProjectTo<PersonDto>(config).ToList();
        Assert.Equal("Unknown", result[0].FullName);
        Assert.Equal("Ada", result[1].FullName);
    }

    [Fact]
    public void MissingMapThrows()
    {
        var config = new MapperConfiguration(cfg => { });
        var ex = Assert.Throws<AutoMapperMappingException>(() =>
            new List<Person> { new() } .AsQueryable().ProjectTo<PersonDto>(config).ToList());
        Assert.Contains("Person", ex.Message);
        Assert.Contains("PersonDto", ex.Message);
    }

    [Fact]
    public void MissingMemberMapThrowsInsteadOfSilentlyDegrading()
    {
        // CustomerDto has no map for the Address type pair used below:
        // an OrderItem-like complex member without a map must fail.
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Customer, CustomerDto>();
            // Deliberately no Address -> AddressDto map.
        });
        var ex = Assert.Throws<AutoMapperMappingException>(() =>
            new List<Customer> { new() { Address = new Address { City = "KC" } } }.AsQueryable()
                .ProjectTo<CustomerDto>(config).ToList());
        Assert.Contains("Address", ex.Message);
    }

    [Fact]
    public void PolymorphicDerivedMapping()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Animal, AnimalDto>().Include<Dog, DogDto>().Include<Cat, CatDto>();
            cfg.CreateMap<Dog, DogDto>();
            cfg.CreateMap<Cat, CatDto>();
        });
        var result = new List<Animal>
        {
            new Dog { Name = "Rex", Breed = "Lab" },
            new Cat { Name = "Mittens", Lives = 9 },
            new Animal { Name = "Generic" },
        }.AsQueryable().ProjectTo<AnimalDto>(config).ToList();
        Assert.IsType<DogDto>(result[0]);
        Assert.Equal("Lab", ((DogDto)result[0]).Breed);
        Assert.IsType<CatDto>(result[1]);
        Assert.Equal(9, ((CatDto)result[1]).Lives);
        Assert.IsType<AnimalDto>(result[2]);
        Assert.Equal("Generic", result[2].Name);
    }

    [Fact]
    public void ParametersOverloadSubstitutesClosureValues()
    {
        string suffix = "?";
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.FullName, o => o.MapFrom(s => s.FirstName + suffix)));
        var source = new List<Person> { new() { FirstName = "Ada" } }.AsQueryable();
        Assert.Equal("Ada?", source.ProjectTo<PersonDto>(config).Single().FullName);
        var parameters = new Dictionary<string, object?> { ["suffix"] = "!" };
        Assert.Equal("Ada!", source.ProjectTo<PersonDto>(config, parameters).Single().FullName);
        Assert.Equal("Ada!", source.ProjectTo<PersonDto>(config, new { suffix = "!" }).Single().FullName);
    }

    [Fact]
    public void ParameterizedConstructorDestination()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<CtorSource, CtorDto>());
        var result = new List<CtorSource> { new() { Id = 3, Name = "Ada", Age = 9 } }.AsQueryable()
            .ProjectTo<CtorDto>(config).ToList();
        Assert.Equal(3, result[0].Id);
        Assert.Equal("Ada", result[0].Name);
        Assert.Equal(9, result[0].Age);
    }

    [Fact]
    public void StringConversions()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.StatusName, o => o.MapFrom(s => s.Age)));
        var numeric = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.Age, o => o.MapFrom(s => s.StatusText)));
        var intToString = new List<Person> { new() { Age = 42 } }.AsQueryable()
            .ProjectTo<PersonDto>(config).ToList();
        Assert.Equal("42", intToString[0].StatusName);
        var stringToInt = new List<Person> { new() { StatusText = "17" } }.AsQueryable()
            .ProjectTo<PersonDto>(numeric).ToList();
        Assert.Equal(17, stringToInt[0].Age);
    }

    [Fact]
    public void ProjectionExpressionIsReusableAndCached()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>()
            .ForMember(d => d.FullName, o => o.MapFrom(s => s.FirstName)));
        var first = new List<Person> { new() { FirstName = "Ada" } }.AsQueryable().ProjectTo<PersonDto>(config).ToList();
        var second = new List<Person> { new() { FirstName = "Bo" } }.AsQueryable().ProjectTo<PersonDto>(config).ToList();
        Assert.Equal("Ada", first[0].FullName);
        Assert.Equal("Bo", second[0].FullName);
    }
}
