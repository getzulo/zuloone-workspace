// ══ ДВИЖОК ПЛАТФОРМЫ — только чтение ══
// Оригинальный MIQS-драйвер, вкомпилированный в ZuloOne. Правки — в исходниках
// платформы (src/ZuloOne.Core/Server/totals/Calculation), не в воркспейсе:
// файл перезаписывается при каждом экспорте и в компиляцию не попадает.


using ZuloOne.ClassDescriptors;
using ZuloOne.Exceptions;
using ZuloOne.Server.Properties;
using ZuloOne.Totals;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ZuloOne.Server.Totals.Calculation
{
    public class StockBaseTotalDriver : FifoTotalDriver
    {
        public StockBaseTotalDriver(
          TotalDescriptor td,
          string quantityVariableName = "Quantity",
          string amountVariableName = "Amount")
          : base(td, quantityVariableName, amountVariableName)
        {
            this.OutcomeLotAmounts = (IDictionary<Decimal, Decimal>)new Dictionary<Decimal, Decimal>();
            this.LotsIndex = (IDictionary<QueueKeyValue, List<DetailedTransactionValue>>)new Dictionary<QueueKeyValue, List<DetailedTransactionValue>>();
            this.PriceProtectKeyDimensions = this.GetPriceProtectKeyDimensionNames();
        }

        private IDictionary<Decimal, Decimal> OutcomeLotAmounts { get; set; }

        protected string[] PriceProtectKeyDimensions { get; set; }

        protected IDictionary<QueueKeyValue, List<DetailedTransactionValue>> LotsIndex { get; set; }

        public override bool IncomeHasHigherPriority(TransactionPair pair) => base.IncomeHasHigherPriority(pair) || this.IsPriceProtect(pair.IncomeValue);

        public override void BeginDocument(DateTime transactionDate, Guid docId)
        {
            base.BeginDocument(transactionDate, docId);
            this.OutcomeLotAmounts.Clear();
        }

        public override void ValidateTransactions(
          TransactionPairCollection transactionPairs,
          TransactionCollection transactions)
        {
            base.ValidateTransactions(transactionPairs, transactions);
            foreach (TransactionPair transactionPair in (Collection<TransactionPair>)transactionPairs)
            {
                TransactionPair pair = transactionPair;
                if (this.IsPriceProtect(pair.IncomeValue))
                {
                    string[] array = pair.OutcomeValue.TotalDescriptor.Dimensions.Where<TotalDimensionDescriptor>((Func<TotalDimensionDescriptor, bool>)(d => !((IEnumerable<string>)this.PriceProtectKeyDimensions).Contains<string>(d.Name) && !pair.OutcomeValue.IsCoordinateNull(d.Name))).Select<TotalDimensionDescriptor, string>((Func<TotalDimensionDescriptor, string>)(d => d.Name)).ToArray<string>();
                    if (((IEnumerable<string>)array).Any<string>())
                        throw new ZuloOneException(Strings.TotalDriver.Stock.NotPriceProtectKeyDimensionNotNull((object)string.Join(", ", array), (object)this.TotalID, (object)pair.OutcomeValue));
                }
            }
        }

        protected override DetailedTransactionValue AddFifoLot(
          TransactionValue transaction)
        {
            DetailedTransactionValue transactionValue = base.AddFifoLot(transaction);
            this.FindOrCreateLots(this.GetPriceProtectKeyValue(transaction)).Add(transactionValue);
            return transactionValue;
        }

        protected override DetailedTransactionValue RemoveFifoLot(
          QueueKeyValue keyValue,
          Queue<DetailedTransactionValue> queue)
        {
            DetailedTransactionValue transaction = base.RemoveFifoLot(keyValue, queue);
            QueueKeyValue priceProtectKeyValue = this.GetPriceProtectKeyValue((TransactionValue)transaction);
            List<DetailedTransactionValue> lots = this.FindLots(priceProtectKeyValue);
            if (lots != null)
            {
                lots.Remove(transaction);
                if (!lots.Any<DetailedTransactionValue>())
                    this.LotsIndex.Remove(priceProtectKeyValue);
            }
            return transaction;
        }

        protected virtual bool IsPriceProtect(TransactionValue tv) => !tv.IsValueNull(this.QuantityVariableName) && tv.GetValue(this.QuantityVariableName) == 0M;

        protected virtual string[] GetPriceProtectKeyDimensionNames() => this.TotalDescriptor.GetKeyDimensionNames();

        protected virtual QueueKeyValue GetPriceProtectKeyValue(
          TransactionValue transaction)
        {
            return new QueueKeyValue(((IEnumerable<string>)this.PriceProtectKeyDimensions).Select<string, Guid>((Func<string, Guid>)(d => transaction.GetCoordinate(d))));
        }

        protected List<DetailedTransactionValue> FindLots(
          QueueKeyValue keyValue)
        {
            List<DetailedTransactionValue> transactionValueList;
            return this.LotsIndex.TryGetValue(keyValue, out transactionValueList) ? transactionValueList : (List<DetailedTransactionValue>)null;
        }

        protected List<DetailedTransactionValue> FindOrCreateLots(
          QueueKeyValue keyValue)
        {
            List<DetailedTransactionValue> orCreateLots = this.FindLots(keyValue);
            if (orCreateLots == null)
            {
                orCreateLots = new List<DetailedTransactionValue>();
                this.LotsIndex.Add(keyValue, orCreateLots);
            }
            return orCreateLots;
        }

        protected List<DetailedTransactionValue> FindLots(
          TransactionValue transaction)
        {
            return this.FindLots(this.GetPriceProtectKeyValue(transaction));
        }

        protected List<DetailedTransactionValue> FindOrCreateLots(
          TransactionValue transaction)
        {
            return this.FindOrCreateLots(this.GetPriceProtectKeyValue(transaction));
        }

        public override ICollection<DetailedTransactionValue> CalculateOutcomes(
          TransactionValue tv,
          IEnumerable<TransactionValue> incomes)
        {
            return this.IsPriceProtect(tv) ? this.CalculatePriceProtectOutcomes(tv, incomes) : base.CalculateOutcomes(tv, incomes);
        }

        public override ICollection<DetailedTransactionValue> CalculateIncomes(
          TransactionValue incomeValue,
          IEnumerable<TransactionValue> outcomes)
        {
            return this.IsPriceProtect(incomeValue) ? this.CalculatePriceProtectIncomes(incomeValue, outcomes) : base.CalculateIncomes(incomeValue, outcomes);
        }

        private ICollection<DetailedTransactionValue> CalculatePriceProtectOutcomes(
          TransactionValue tv,
          IEnumerable<TransactionValue> incomes)
        {
            List<DetailedTransactionValue> priceProtectOutcomes = new List<DetailedTransactionValue>();
            Decimal num1 = -tv.GetValue(this.AmountVariableName);
            List<DetailedTransactionValue> lots = this.FindLots(tv);
            if (lots != null && lots.Any<DetailedTransactionValue>())
            {
                Decimal num2 = lots.Sum<DetailedTransactionValue>((Func<DetailedTransactionValue, Decimal>)(d => d.GetValue(this.QuantityVariableName)));
                lots.Sum<DetailedTransactionValue>((Func<DetailedTransactionValue, Decimal>)(d => d.GetValue(this.AmountVariableName)));
                Decimal num3 = num1 / num2;
                foreach (DetailedTransactionValue lot in lots)
                {
                    Decimal num4 = lot.GetValue(this.QuantityVariableName);
                    Decimal num5 = lot.GetValue(this.AmountVariableName);
                    Decimal num6 = num3 * num4;
                    if (num5 >= num6)
                    {
                        lot.SetValue(this.AmountVariableName, num5 - num6);
                        this.OutcomeLotAmounts[lot.LotNo] = 0M;
                    }
                    else
                    {
                        lot.SetValue(this.AmountVariableName, 0M);
                        this.OutcomeLotAmounts[lot.LotNo] = num6 - num5;
                    }
                    DetailedTransactionValue transactionValue = this.CreateDetailedTransactionValue(tv, lot);
                    transactionValue.SetValue(this.AmountVariableName, -1M * num6);
                    priceProtectOutcomes.Add(transactionValue);
                }
            }
            else
            {
                DetailedTransactionValue transactionValue = this.CreateDetailedTransactionValue(tv);
                priceProtectOutcomes.Add(transactionValue);
                this.OutcomeLotAmounts[transactionValue.LotNo] = num1;
            }
            return (ICollection<DetailedTransactionValue>)priceProtectOutcomes;
        }

        private ICollection<DetailedTransactionValue> CalculatePriceProtectIncomes(
          TransactionValue incomeValue,
          IEnumerable<TransactionValue> outcomes)
        {
            List<DetailedTransactionValue> priceProtectIncomes = new List<DetailedTransactionValue>();
            List<DetailedTransactionValue> lots = this.FindLots(incomeValue);
            if (lots != null && lots.Any<DetailedTransactionValue>())
            {
                foreach (DetailedTransactionValue lot in lots)
                {
                    DetailedTransactionValue transactionValue = this.CreateIncomeDetailedTransactionValue(incomeValue, lot);
                    if (transactionValue != null)
                        priceProtectIncomes.Add(transactionValue);
                }
            }
            else
            {
                DetailedTransactionValue transactionValue = this.CreateIncomeDetailedTransactionValue(incomeValue);
                if (transactionValue != null)
                    priceProtectIncomes.Add(transactionValue);
            }
            return (ICollection<DetailedTransactionValue>)priceProtectIncomes;
        }

        private DetailedTransactionValue CreateDetailedTransactionValue(
          TransactionValue tv,
          DetailedTransactionValue lot = null)
        {
            Decimal lotNo = this.GetLotNo(lot);
            DetailedTransactionValue transactionValue = tv.CreateDetailedTransactionValue();
            transactionValue.LotNo = lotNo;
            transactionValue.DeltaSubNo = this.GetNextDeltaSubNo(transactionValue.DeltaNo);
            this.CopyAllCoordinatesFromLot(transactionValue, lot);
            return transactionValue;
        }

        private DetailedTransactionValue CreateIncomeDetailedTransactionValue(
          TransactionValue incomeValue,
          DetailedTransactionValue lot = null)
        {
            Decimal lotNo = this.GetLotNo(lot);
            Decimal num = 0M;
            if (this.OutcomeLotAmounts == null || !this.OutcomeLotAmounts.TryGetValue(lotNo, out num))
                return (DetailedTransactionValue)null;
            DetailedTransactionValue transactionValue = this.CreateDetailedTransactionValue(incomeValue, lot);
            transactionValue.SetValue(this.AmountVariableName, num);
            this.OutcomeLotAmounts.Remove(lotNo);
            return transactionValue;
        }

        private Decimal GetLotNo(DetailedTransactionValue lot) => lot != null ? lot.LotNo : -1M;

        private void CopyAllCoordinatesFromLot(
          DetailedTransactionValue transaction,
          DetailedTransactionValue lot)
        {
            if (lot == null)
                return;
            foreach (string dimensionName in transaction.TotalDescriptor.Dimensions.Select<TotalDimensionDescriptor, string>((Func<TotalDimensionDescriptor, string>)(d => d.Name)))
                transaction.SetCoordinate(dimensionName, lot.GetCoordinate(dimensionName));
        }
    }
}
