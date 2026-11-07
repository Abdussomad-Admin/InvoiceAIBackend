namespace Domain.Bills;

public enum BillStatus
{
    Draft = 0,
    Open = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Void = 4
}