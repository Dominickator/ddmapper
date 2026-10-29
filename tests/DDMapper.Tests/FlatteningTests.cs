using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Flattening: nested source paths collapse into flat destination members,
/// including three-level chains and null intermediates.</summary>
public class FlatteningTests
{
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
    }

    public class OrderDto
    {
        public int Id { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerAddressCity { get; set; }
        public string? CustomerAddressZip { get; set; }
    }

    [Fact]
    public void TwoLevelFlattening()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>()).CreateMapper();
        var dest = mapper.Map<OrderDto>(new Order { Id = 1, Customer = new Customer { Name = "Ada" } });
        Assert.Equal(1, dest.Id);
        Assert.Equal("Ada", dest.CustomerName);
    }

    [Fact]
    public void ThreeLevelFlattening()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>()).CreateMapper();
        var dest = mapper.Map<OrderDto>(new Order
        {
            Customer = new Customer { Address = new Address { City = "Blue Springs", Zip = "64015" } }
        });
        Assert.Equal("Blue Springs", dest.CustomerAddressCity);
        Assert.Equal("64015", dest.CustomerAddressZip);
    }

    [Fact]
    public void NullIntermediateYieldsNullFlatMember()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>()).CreateMapper();
        var dest = mapper.Map<OrderDto>(new Order { Customer = null });
        Assert.Null(dest.CustomerName);
        Assert.Null(dest.CustomerAddressCity);
    }

    [Fact]
    public void NullLeafYieldsNullFlatMember()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>()).CreateMapper();
        var dest = mapper.Map<OrderDto>(new Order { Customer = new Customer { Name = null, Address = new Address { City = null } } });
        Assert.Null(dest.CustomerName);
        Assert.Null(dest.CustomerAddressCity);
    }

    [Fact]
    public void FlattenedMemberCanBeOverriddenExplicitly()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Order, OrderDto>()
                .ForMember(d => d.CustomerName, o => o.MapFrom(s => s.Customer == null ? "none" : s.Customer.Name + "!"))
        ).CreateMapper();
        Assert.Equal("none", mapper.Map<OrderDto>(new Order()).CustomerName);
        Assert.Equal("Ada!", mapper.Map<OrderDto>(new Order { Customer = new Customer { Name = "Ada" } }).CustomerName);
    }
}
