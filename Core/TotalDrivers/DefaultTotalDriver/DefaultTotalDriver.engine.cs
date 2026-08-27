// ══ ДВИЖОК ПЛАТФОРМЫ — только чтение ══
// Оригинальный MIQS-драйвер, вкомпилированный в ZuloOne. Правки — в исходниках
// платформы (src/ZuloOne.Core/Server/totals/Calculation), не в воркспейсе:
// файл перезаписывается при каждом экспорте и в компиляцию не попадает.


using ZuloOne.ClassDescriptors;
using ZuloOne.Exceptions;
using ZuloOne.Server.Properties;
using ZuloOne.Toolbox;
using ZuloOne.Totals;
using ZuloOne.Totals.Calculation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ZuloOne.Server.Totals.Calculation
{
    public class DefaultTotalDriver : ITotalDriver, ITransactionValidator
    {
        private long negativeDeltaSubNo;
        private long positiveDeltaSubNo;

        public DefaultTotalDriver(TotalDescriptor td) => this.TotalDescriptor = td;

        public Guid TotalID => this.TotalDescriptor.Guid;

        protected TotalDescriptor TotalDescriptor { get; private set; }

        protected ITotalCalculator TotalCalculator { get; private set; }

        public DateTime LimitDateTime { get; protected set; }

        public Guid LimitDocumentID { get; protected set; }

        public Decimal DocumentBalance { get; protected set; }

        public DateTime CurrentDateTime { get; protected set; }

        public Guid CurrentDocumentID { get; protected set; }

        ICollection<DetailedTransactionValue> ITotalDriver.DetailedTransactions { get; } = (ICollection<DetailedTransactionValue>)new List<DetailedTransactionValue>();

        protected void AddDocumentBalance(Decimal balance) => this.DocumentBalance += balance;

        protected virtual void AddError(TransactionValue value, string format, params object[] args) => this.TotalCalculator.AddError(value, format, args);

        protected void AddCriticalError(TransactionValue value, string format, params object[] args) => this.TotalCalculator.AddCriticalError(value, format, args);

        public void BeginCalculation(ITotalCalculator processor) => this.TotalCalculator = processor;

        public void EndCalculation()
        {
        }

        public virtual void BeginDocument(DateTime transactionDate, Guid docId)
        {
            this.DocumentBalance = 0M;
            this.CurrentDateTime = transactionDate;
            this.CurrentDocumentID = docId;
        }

        public virtual void EndDocument(DateTime transactionDate, Guid docId)
        {
            this.LimitDateTime = transactionDate;
            this.LimitDocumentID = docId;
        }

        public virtual void BeginTransaction() => this.negativeDeltaSubNo = this.positiveDeltaSubNo = 0L;

        protected long GetNextDeltaSubNo(long deltaNo) => deltaNo < 0L ? this.negativeDeltaSubNo++ : this.positiveDeltaSubNo++;

        protected ICollection<DetailedTransactionValue> AssignDeltaSubNos(
          ICollection<DetailedTransactionValue> transactions)
        {
            foreach (DetailedTransactionValue transaction in (IEnumerable<DetailedTransactionValue>)transactions)
                transaction.DeltaSubNo = this.GetNextDeltaSubNo(transaction.DeltaNo);
            return transactions;
        }

        public virtual void EndTransaction()
        {
        }

        public virtual void LoadTotalState(
          ITransactionLoader loader,
          DateTime transactionDate,
          Guid docId)
        {
        }

        public void AddCompleteTransaction(DetailedTransactionValue transaction)
        {
            if (!transaction.IsComplete)
                this.AddCriticalError((TransactionValue)transaction, "Cannot apply an incomplete transaction: {0}, SKIPPED!", (object)transaction);
            else
                ((ITotalDriver)this).DetailedTransactions.Add(transaction);
        }

        protected IEnumerable<DetailedTransactionValue> FindDetailedTransactions(
          Func<DetailedTransactionValue, bool> predicate)
        {
            return ((ITotalDriver)this).DetailedTransactions.Where<DetailedTransactionValue>(predicate).Select<DetailedTransactionValue, DetailedTransactionValue>((Func<DetailedTransactionValue, DetailedTransactionValue>)(t => t.Clone()));
        }

        protected IEnumerable<T> FindDetailedTransactions<T>(Func<T, bool> predicate) where T : DetailedTransactionValue => ((ITotalDriver)this).DetailedTransactions.OfType<T>().Where<T>(predicate).Select<T, T>((Func<T, T>)(t => t.Clone() as T));

        public virtual bool HasHighPriority => false;

        public virtual bool IncomeHasHigherPriority(TransactionPair pair) => this.HasHighPriority;

        protected virtual bool CheckVariableValues => true;

        public virtual ICollection<DetailedTransactionValue> CalculateIncomes(
          TransactionValue incomeValue,
          IEnumerable<TransactionValue> outcomes)
        {
            List<DetailedTransactionValue> incomes = new List<DetailedTransactionValue>();
            if (!outcomes.Any<TransactionValue>())
            {
                this.AddCriticalError(incomeValue, "Cannot calculate income transactions without outcomes.");
                return (ICollection<DetailedTransactionValue>)incomes;
            }
            TransactionValue transactionValue = incomeValue.Clone();
            HashSet<string> stringSet = new HashSet<string>();
            foreach (TransactionValue outcome in outcomes)
            {
                DetailedTransactionValue income = incomeValue.CreateDetailedTransactionValue();
                income.DeltaSubNo = this.GetNextDeltaSubNo(income.DeltaNo);
                foreach (TotalDimensionDescriptor dimensionDescriptor1 in income.TotalDescriptor.Dimensions.Where<TotalDimensionDescriptor>((Func<TotalDimensionDescriptor, bool>)(d => !d.IsOperational && income.IsCoordinateNull(d.Name))))
                {
                    TotalDimensionDescriptor incomeDim = dimensionDescriptor1;
                    TotalDimensionDescriptor dimensionDescriptor2 = outcome.TotalDescriptor.Dimensions.FirstOrDefault<TotalDimensionDescriptor>((Func<TotalDimensionDescriptor, bool>)(d => d.Name == incomeDim.Name));
                    if (dimensionDescriptor2 == null)
                    {
                        this.AddCriticalError((TransactionValue)income, "Cannot calculate income because outcome transaction doesn't have dimension {0}, iv={1}.", (object)incomeDim.Name, (object)income);
                        return (ICollection<DetailedTransactionValue>)incomes;
                    }
                    if (outcome.IsCoordinateNull(dimensionDescriptor2.Name))
                    {
                        this.AddCriticalError((TransactionValue)income, "Cannot calculate income because outcome transaction coordinate isn't set: {0}, iv={1}", (object)incomeDim.Name, (object)income);
                        return (ICollection<DetailedTransactionValue>)incomes;
                    }
                    income.SetCoordinate(incomeDim.Name, outcome.GetCoordinate(dimensionDescriptor2.Name));
                }
                foreach (TotalVariableDescriptor variable in (IEnumerable<TotalVariableDescriptor>)income.TotalDescriptor.Variables)
                {
                    TotalVariableDescriptor incomeVar = variable;
                    TotalVariableDescriptor variableDescriptor = outcome.TotalDescriptor.Variables.FirstOrDefault<TotalVariableDescriptor>((Func<TotalVariableDescriptor, bool>)(v => v.Name == incomeVar.Name));
                    if (variableDescriptor == null || outcome.IsValueNull(variableDescriptor.Name))
                    {
                        if (income.IsValueNull(incomeVar.Name))
                        {
                            this.AddCriticalError((TransactionValue)income, "Cannot calculate income because outcome value is not available: {0}, iv={1}, ov={2}", (object)incomeVar.Name, (object)income, (object)outcome);
                            return (ICollection<DetailedTransactionValue>)incomes;
                        }
                    }
                    else if (income.IsValueNull(incomeVar.Name))
                        income.SetValue(incomeVar.Name, -outcome.GetValue(variableDescriptor.Name));
                    else if (outcomes.Count<TransactionValue>() == 1)
                    {
                        Decimal num1 = income.GetValue(incomeVar.Name);
                        Decimal num2 = outcome.GetValue(variableDescriptor.Name);
                        if (num1 + num2 != 0M && this.CheckVariableValues)
                            this.AddError((TransactionValue)income, "Income/outcome variable {0} mismatch:\r\n\t\t\t\t\t\t\t|Income = {1},\r\n\t\t\t\t\t\t\t|Outcome = {2}".RemoveHeadingSpaces(), (object)incomeVar.Name, (object)income, (object)outcome);
                        income.SetValue(incomeVar.Name, -num2);
                    }
                    else
                    {
                        Decimal num3 = transactionValue.GetValue(incomeVar.Name);
                        Decimal num4 = -outcome.GetValue(variableDescriptor.Name);
                        income.SetValue(incomeVar.Name, num4);
                        transactionValue.SetValue(incomeVar.Name, num3 - num4);
                        stringSet.Add(incomeVar.Name);
                    }
                }
                incomes.Add(income);
            }
            if (this.CheckVariableValues)
            {
                foreach (string variableName in stringSet)
                {
                    if (transactionValue.GetValue(variableName) != 0M)
                        this.AddCriticalError(incomeValue, "Cannot distribute variable {0}: iv={1}, ov.Count={2}", (object)variableName, (object)incomeValue, (object)outcomes.Count<TransactionValue>());
                }
            }
            return (ICollection<DetailedTransactionValue>)incomes;
        }

        public virtual ICollection<DetailedTransactionValue> CalculateOutcomes(
          TransactionValue outcomeValue,
          IEnumerable<TransactionValue> incomes)
        {
            List<DetailedTransactionValue> outcomes = new List<DetailedTransactionValue>();
            if (!incomes.Any<TransactionValue>())
            {
                this.AddCriticalError(outcomeValue, "Cannot calculate outcome transaction without incomes.");
                return (ICollection<DetailedTransactionValue>)outcomes;
            }
            TransactionValue transactionValue = outcomeValue.Clone();
            HashSet<string> stringSet = new HashSet<string>();
            foreach (TransactionValue income in incomes)
            {
                DetailedTransactionValue outcome = outcomeValue.CreateDetailedTransactionValue();
                outcome.DeltaSubNo = this.GetNextDeltaSubNo(outcome.DeltaNo);
                foreach (TotalDimensionDescriptor dimensionDescriptor1 in outcome.TotalDescriptor.Dimensions.Where<TotalDimensionDescriptor>((Func<TotalDimensionDescriptor, bool>)(d => !d.IsOperational && outcome.IsCoordinateNull(d.Name))))
                {
                    TotalDimensionDescriptor outcomeDim = dimensionDescriptor1;
                    TotalDimensionDescriptor dimensionDescriptor2 = income.TotalDescriptor.Dimensions.FirstOrDefault<TotalDimensionDescriptor>((Func<TotalDimensionDescriptor, bool>)(d => d.Name == outcomeDim.Name));
                    if (dimensionDescriptor2 == null)
                    {
                        this.AddCriticalError((TransactionValue)outcome, "Cannot calculate outcome because income transaction doesn't have dimension {0}, ov={1}.", (object)outcomeDim.Name, (object)outcome);
                        return (ICollection<DetailedTransactionValue>)outcomes;
                    }
                    if (income.IsCoordinateNull(dimensionDescriptor2.Name))
                    {
                        this.AddCriticalError((TransactionValue)outcome, "Cannot calculate outcome because income transaction coordinate isn't set: {0}, ov={1}", (object)outcomeDim.Name, (object)outcome);
                        return (ICollection<DetailedTransactionValue>)outcomes;
                    }
                    outcome.SetCoordinate(outcomeDim.Name, income.GetCoordinate(dimensionDescriptor2.Name));
                }
                foreach (TotalVariableDescriptor variable in (IEnumerable<TotalVariableDescriptor>)outcome.TotalDescriptor.Variables)
                {
                    TotalVariableDescriptor outcomeVar = variable;
                    TotalVariableDescriptor variableDescriptor = income.TotalDescriptor.Variables.FirstOrDefault<TotalVariableDescriptor>((Func<TotalVariableDescriptor, bool>)(v => v.Name == outcomeVar.Name));
                    if (variableDescriptor == null || income.IsValueNull(variableDescriptor.Name))
                    {
                        if (outcome.IsValueNull(outcomeVar.Name))
                        {
                            this.AddCriticalError((TransactionValue)outcome, "Cannot calculate outcome because income value is not available: {0}, ov={1}, iv={2}", (object)outcomeVar.Name, (object)outcome, (object)income);
                            return (ICollection<DetailedTransactionValue>)outcomes;
                        }
                    }
                    else if (outcome.IsValueNull(outcomeVar.Name))
                        outcome.SetValue(outcomeVar.Name, -income.GetValue(variableDescriptor.Name));
                    else if (incomes.Count<TransactionValue>() == 1)
                    {
                        Decimal num1 = outcome.GetValue(outcomeVar.Name);
                        Decimal num2 = income.GetValue(variableDescriptor.Name);
                        if (num2 + num1 != 0M && this.CheckVariableValues)
                            this.AddError((TransactionValue)outcome, "Income/outcome variable {0} mismatch: iv={1}, ov={2}", (object)outcomeVar.Name, (object)income, (object)outcome);
                        outcome.SetValue(outcomeVar.Name, -num2);
                    }
                    else
                    {
                        Decimal num3 = transactionValue.GetValue(outcomeVar.Name);
                        Decimal num4 = -income.GetValue(variableDescriptor.Name);
                        outcome.SetValue(outcomeVar.Name, num4);
                        transactionValue.SetValue(outcomeVar.Name, num3 - num4);
                        stringSet.Add(outcomeVar.Name);
                    }
                }
                outcomes.Add(outcome);
            }
            foreach (string variableName in stringSet)
            {
                if (transactionValue.GetValue(variableName) != 0M)
                    this.AddCriticalError(outcomeValue, "Cannot distribute variable {0}: ov={1}, iv.Count={2}", (object)variableName, (object)outcomeValue, (object)incomes.Count<TransactionValue>());
            }
            return (ICollection<DetailedTransactionValue>)outcomes;
        }

        public virtual ICollection<DetailedTransactionValue> RecalculateIncomes(
          IEnumerable<DetailedTransactionValue> incomes,
          IEnumerable<DetailedTransactionValue> outcomes)
        {
            return (ICollection<DetailedTransactionValue>)incomes.ToList<DetailedTransactionValue>();
        }

        public virtual void ValidateTransactions(
          TransactionPairCollection transactionPairs,
          TransactionCollection transactions)
        {
            foreach (TransactionPair transactionPair in (Collection<TransactionPair>)transactionPairs)
            {
                if (transactionPair.OutcomeValue.PairTotalID == this.TotalID)
                    this.ValidateAmount(transactionPair.IncomeValue);
                if (transactionPair.IncomeValue.PairTotalID == this.TotalID)
                    this.ValidateAmount(transactionPair.OutcomeValue);
            }
        }

        protected virtual void ValidateAmount(TransactionValue transaction)
        {
            if (transaction.IsValueNull("Amount"))
                return;
            Decimal num = transaction.GetValue("Amount");
            if (Math.Round(num, 2) != num)
                throw new ZuloOneException(Strings.TotalDriver.UnsupportedAmountPrecision((object)num, (object)2, (object)this.TotalID, (object)transaction));
        }
    }
}
