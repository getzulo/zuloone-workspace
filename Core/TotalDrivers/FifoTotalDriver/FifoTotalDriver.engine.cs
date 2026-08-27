// ══ ДВИЖОК ПЛАТФОРМЫ — только чтение ══
// Оригинальный MIQS-драйвер, вкомпилированный в ZuloOne. Правки — в исходниках
// платформы (src/ZuloOne.Core/Server/totals/Calculation), не в воркспейсе:
// файл перезаписывается при каждом экспорте и в компиляцию не попадает.


using ZuloOne.ClassDescriptors;
using ZuloOne.Metadata;
using ZuloOne.Totals;
using ZuloOne.Totals.Calculation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZuloOne.Server.Totals.Calculation
{
    public class FifoTotalDriver : DefaultTotalDriver
    {
        private int currentDocumentLotNumber;

        public FifoTotalDriver(
          TotalDescriptor td,
          string quantityVariableName = "Quantity",
          string amountVariableName = "Amount")
          : base(td)
        {
            this.QuantityVariableName = quantityVariableName;
            this.AmountVariableName = amountVariableName;
            this.QueueIndex = new Dictionary<QueueKeyValue, Queue<DetailedTransactionValue>>();
            this.KeyDimensions = td.GetKeyDimensionNames();
            if (!td.Variables.Any<TotalVariableDescriptor>((Func<TotalVariableDescriptor, bool>)(v => v.Name == this.QuantityVariableName)) && !td.Variables.Any<TotalVariableDescriptor>((Func<TotalVariableDescriptor, bool>)(v => v.Name == this.AmountVariableName)))
                throw new ArgumentException(string.Format("FifoTotalDriver requires {0} and {1} variables in {2} total.", (object)this.QuantityVariableName, (object)this.AmountVariableName, (object)this.TotalDescriptor.Name), nameof(td));
        }

        public string QuantityVariableName { get; private set; }

        public string AmountVariableName { get; private set; }

        protected string[] KeyDimensions { get; private set; }

        protected Dictionary<QueueKeyValue, Queue<DetailedTransactionValue>> QueueIndex { get; set; }

        protected virtual QueueKeyValue GetKeyValue(TransactionValue transaction) => new QueueKeyValue(((IEnumerable<string>)this.KeyDimensions).Select<string, Guid>((Func<string, Guid>)(d => transaction.GetCoordinate(d))));

        protected Queue<DetailedTransactionValue> FindQueue(
          QueueKeyValue keyValue)
        {
            Queue<DetailedTransactionValue> transactionValueQueue;
            return this.QueueIndex.TryGetValue(keyValue, out transactionValueQueue) ? transactionValueQueue : (Queue<DetailedTransactionValue>)null;
        }

        protected Queue<DetailedTransactionValue> FindOrCreateQueue(
          QueueKeyValue keyValue)
        {
            Queue<DetailedTransactionValue> orCreateQueue = this.FindQueue(keyValue);
            if (orCreateQueue == null)
            {
                orCreateQueue = new Queue<DetailedTransactionValue>();
                this.QueueIndex.Add(keyValue, orCreateQueue);
            }
            return orCreateQueue;
        }

        protected Queue<DetailedTransactionValue> FindQueue(
          TransactionValue transaction)
        {
            return this.FindQueue(this.GetKeyValue(transaction));
        }

        protected Queue<DetailedTransactionValue> FindOrCreateQueue(
          TransactionValue transaction)
        {
            return this.FindOrCreateQueue(this.GetKeyValue(transaction));
        }

        public override void LoadTotalState(
          ITransactionLoader loader,
          DateTime limitDate,
          Guid limitDocId)
        {
            foreach (DetailedTransactionValue transaction in loader.LoadTotalState(limitDate, limitDocId))
                this.FindOrCreateQueue((TransactionValue)transaction).Enqueue(transaction);
        }

        public override void BeginDocument(DateTime transactionDate, Guid docId)
        {
            base.BeginDocument(transactionDate, docId);
            this.currentDocumentLotNumber = 0;
        }

        protected int GetNextDocumentLotNumber()
        {
            ++this.currentDocumentLotNumber;
            return this.currentDocumentLotNumber - 1;
        }

        protected Decimal GetLotNo()
        {
            // Guid documents cannot be bit-packed like the original long ids;
            // seconds since 1900 shifted by 16 bits + a per-document counter
            // keeps the tag unique and time-ordered within a calculation.
            DateTime dateTime = this.CurrentDateTime == default ? DateTime.UtcNow : this.CurrentDateTime;
            long d1 = dateTime.AddYears(-1900).Ticks / 10000000L & 137438953471L;
            int documentLotNumber = this.GetNextDocumentLotNumber();
            if (documentLotNumber > (int)ushort.MaxValue)
                throw new ArgumentException("Document " + this.CurrentDocumentID.ToString() + " generates too many FIFO lots: " + documentLotNumber.ToString());
            return this.ShiftLeft((Decimal)d1, 16) + (Decimal)documentLotNumber;
        }

        private Decimal ShiftLeft(Decimal d, int bits)
        {
            for (int index = 0; index < bits; ++index)
                d *= 2M;
            return d;
        }

        protected virtual DetailedTransactionValue AddFifoLot(
          TransactionValue transaction)
        {
            QueueKeyValue keyValue = this.GetKeyValue(transaction);
            DetailedTransactionValue transactionValue = transaction.CreateDetailedTransactionValue();
            transactionValue.LotNo = this.GetLotNo();
            Queue<DetailedTransactionValue> transactionValueQueue = this.FindQueue(keyValue);
            if (transactionValueQueue == null)
            {
                transactionValueQueue = new Queue<DetailedTransactionValue>();
                this.QueueIndex.Add(keyValue, transactionValueQueue);
            }
            transactionValueQueue.Enqueue(transactionValue);
            return transactionValue;
        }

        protected virtual DetailedTransactionValue RemoveFifoLot(
          QueueKeyValue keyValue,
          Queue<DetailedTransactionValue> queue)
        {
            DetailedTransactionValue transactionValue = queue.Dequeue();
            if (!queue.Any<DetailedTransactionValue>())
                this.QueueIndex.Remove(keyValue);
            return transactionValue;
        }

        protected virtual Decimal CalculatePartialAmount(
          Decimal lotQuantity,
          Decimal lotAmount,
          Decimal transQuantity)
        {
            return lotAmount / lotQuantity * transQuantity;
        }

        public override ICollection<DetailedTransactionValue> CalculateIncomes(
          TransactionValue incomeValue,
          IEnumerable<TransactionValue> outcomes)
        {
            TransactionValue transactionValue = outcomes.FirstOrDefault<TransactionValue>();
            foreach (TotalDimensionDescriptor dimensionDescriptor in incomeValue.TotalDescriptor.Dimensions.Where<TotalDimensionDescriptor>((Func<TotalDimensionDescriptor, bool>)(d => !d.IsOperational && incomeValue.IsCoordinateNull(d.Name) && d.ClassDescriptor.ID == Document.StaticClassDescriptor.ID)))
            {
                TotalDimensionDescriptor dim = dimensionDescriptor;
                if (transactionValue == null || !transactionValue.TotalDescriptor.Dimensions.Any<TotalDimensionDescriptor>((Func<TotalDimensionDescriptor, bool>)(d => d.Name == dim.Name)) || transactionValue.IsCoordinateNull(dim.Name))
                    incomeValue.SetCoordinate(dim.Name, incomeValue.DocumentID);
            }
            ICollection<DetailedTransactionValue> incomes = base.CalculateIncomes(incomeValue, outcomes);
            foreach (DetailedTransactionValue transaction in (IEnumerable<DetailedTransactionValue>)incomes)
            {
                if (transaction.IsComplete)
                    transaction.LotNo = this.AddFifoLot((TransactionValue)transaction).LotNo;
            }
            return incomes;
        }

        public override ICollection<DetailedTransactionValue> CalculateOutcomes(
          TransactionValue tv,
          IEnumerable<TransactionValue> incomes)
        {
            if (tv.IsComplete)
                this.AddError(tv, "Complete outcome transaction on FIFO total {0}: {1}, RECALCULATED.", (object)this.TotalDescriptor.Name, (object)tv);
            List<DetailedTransactionValue> outcomes = new List<DetailedTransactionValue>();
            Decimal num1 = -tv.GetValue(this.QuantityVariableName);
            if (num1 <= 0M)
            {
                this.AddCriticalError(tv, "Bad quantity to write off the FIFO total {0}: ov={1}.", (object)this.TotalDescriptor.Name, (object)tv);
                return (ICollection<DetailedTransactionValue>)outcomes;
            }
            QueueKeyValue keyValue = this.GetKeyValue(tv);
            Queue<DetailedTransactionValue> transactionValueQueue = this.FindQueue(keyValue) ?? new Queue<DetailedTransactionValue>();
            while (transactionValueQueue.Any<DetailedTransactionValue>() && num1 > 0M)
            {
                DetailedTransactionValue transactionValue1 = transactionValueQueue.Peek();
                Decimal lotQuantity = transactionValue1.GetValue(this.QuantityVariableName);
                Decimal lotAmount = transactionValue1.GetValue(this.AmountVariableName);
                Decimal transQuantity;
                Decimal num2;
                if (lotQuantity > num1)
                {
                    transQuantity = num1;
                    num2 = this.CalculatePartialAmount(lotQuantity, lotAmount, transQuantity);
                }
                else
                {
                    transQuantity = lotQuantity;
                    num2 = lotAmount;
                }
                DetailedTransactionValue transactionValue2 = tv.CreateDetailedTransactionValue();
                transactionValue2.SetValue(this.QuantityVariableName, -transQuantity);
                transactionValue2.SetValue(this.AmountVariableName, -num2);
                transactionValue2.LotNo = transactionValue1.LotNo;
                transactionValue2.DeltaSubNo = this.GetNextDeltaSubNo(transactionValue2.DeltaNo);
                foreach (string dimensionName in transactionValue2.TotalDescriptor.Dimensions.Where<TotalDimensionDescriptor>((Func<TotalDimensionDescriptor, bool>)(d => !d.IsOperational)).Select<TotalDimensionDescriptor, string>((Func<TotalDimensionDescriptor, string>)(d => d.Name)))
                    transactionValue2.SetCoordinate(dimensionName, transactionValue1.GetCoordinate(dimensionName));
                outcomes.Add(transactionValue2);
                num1 -= transQuantity;
                Decimal num3 = lotQuantity - transQuantity;
                Decimal num4 = lotAmount - num2;
                if (num3 > 0M)
                {
                    transactionValue1.SetValue(this.QuantityVariableName, num3);
                    transactionValue1.SetValue(this.AmountVariableName, num4);
                }
                else
                    this.RemoveFifoLot(keyValue, transactionValueQueue);
            }
            if (num1 > 0M)
            {
                this.AddError(tv, "Write off below zero: ov={0}", (object)tv);
                DetailedTransactionValue transactionValue = tv.CreateDetailedTransactionValue();
                transactionValue.SetValue(this.QuantityVariableName, -num1);
                transactionValue.SetValue(this.AmountVariableName, 0M);
                transactionValue.LotNo = -1M;
                transactionValue.DeltaSubNo = this.GetNextDeltaSubNo(transactionValue.DeltaNo);
                foreach (TotalDimensionDescriptor dimension in (IEnumerable<TotalDimensionDescriptor>)transactionValue.TotalDescriptor.Dimensions)
                {
                    if (transactionValue.IsCoordinateNull(dimension.Name))
                    {
                        if (dimension.ClassDescriptor.ID == Document.StaticClassDescriptor.ID)
                            transactionValue.SetCoordinate(dimension.Name, transactionValue.DocumentID);
                        else
                            this.AddCriticalError(tv, "Cannot calculate non-document dimension: {0}, ov={1}", (object)dimension.Name, (object)tv);
                    }
                }
                outcomes.Add(transactionValue);
            }
            return (ICollection<DetailedTransactionValue>)outcomes;
        }
    }
}
