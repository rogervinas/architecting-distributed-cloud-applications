
using System;
namespace DbWriter
{ 
 public class InventoryRecord 
 { 
  public Guid ItemId { get; set; } 
  public string ItemName { get; set; } 
  public int AvailableItems { get; set; } 
 }
}