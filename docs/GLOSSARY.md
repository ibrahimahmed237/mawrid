# Glossary

The domain words we use in the code, the API and the UI. The code uses the English word. Users of the Arabic UI see the Arabic word.

| Term | Arabic | Code name | Meaning |
|---|---|---|---|
| Organization | المنشأة | `Organization` | The buyer company (tenant) |
| Cost center | مركز التكلفة | `CostCenter` | Department or budget owner that spend is charged to |
| Purchase requisition (PR) | طلب شراء داخلي | `PurchaseRequisition` | An employee's internal request to buy something |
| Request for quotation (RFQ) | طلب عرض سعر | `Rfq` | A request to selected suppliers to give prices for a list of items |
| Quotation | عرض سعر | `Quotation` | A supplier's priced response to an RFQ |
| Sealed bid | عرض مغلق | — | Quotation contents are hidden from the buyer until the RFQ closes |
| Award | ترسية | `Award` | Picking the winning supplier for each RFQ line |
| Purchase order (PO) | أمر شراء | `PurchaseOrder` | A binding order sent to a supplier |
| Goods receipt (GRN) | محضر استلام | `GoodsReceipt` | A record of what was actually delivered and accepted |
| Supplier invoice | فاتورة المورد | `SupplierInvoice` | The supplier's tax invoice for delivered goods or services |
| Three-way match | المطابقة الثلاثية | `InvoiceMatcher` | Checking invoice vs PO vs goods receipt within tolerances |
| On hold | معلّقة | `InvoiceStatus.OnHold` | Invoice blocked from payment until AP resolves the listed reasons |
| Approval matrix | مصفوفة الصلاحيات | `ApprovalPolicy` | Rules that decide who must approve, based on amount, cost center or category |
| Delegation | تفويض | `ApprovalDelegation` | An approver handing authority to someone else for a period |
| Segregation of duties | فصل المهام | — | The person who requests or creates something cannot approve it |
| VAT number | الرقم الضريبي | `VatNumber` | 15-digit KSA VAT registration number |
| Commercial registration (CR) | السجل التجاري | `CrNumber` | Company registration number |
| IBAN | رقم الآيبان | `Iban` | Bank account number; KSA format is `SA` + 22 characters |
| ZATCA | هيئة الزكاة والضريبة والجمارك | — | Zakat, Tax and Customs Authority |
| FATOORA | فاتورة | — | ZATCA's e-invoicing program and platform |
| Clearance | الاعتماد (التخليص) | — | ZATCA validating a B2B invoice before it is shared with the buyer |
| Tolerance | نسبة التجاوز المسموح | `MatchTolerance` | Allowed price or quantity difference before an invoice is held |
| Direct purchase | الشراء المباشر | `SourcingRoute.DirectPurchase` | Buying a small requisition from one supplier without an RFQ, under the direct purchase limit |
| Single-source reason | مبرر المورد الوحيد | `SingleSourceReason` | Why an RFQ invites fewer suppliers than the minimum |
| Acknowledgement | تأكيد استلام أمر الشراء | `PurchaseOrderStatus.Acknowledged` | The supplier confirms it received the PO |
| Short close | الإقفال الجزئي | `PurchaseOrder.ClosedReason` | Closing a partly received PO because the rest will not come |
| Override | تجاوز الحجز بمبرر | `InvoiceHold.OverrideReason` | Accepting one invoice hold with a written reason |
| Recheck | إعادة الفحص | `RecheckInvoiceCommand` | Running the invoice checks again after the cause may be fixed |
| E-invoice (UBL) | الفاتورة الإلكترونية | `UblInvoiceReader` | The supplier's ZATCA invoice as UBL 2.1 XML |
| Webhook | إشعار آلي للنظام المحاسبي | `WebhookDelivery` | A signed HTTP call that tells the ERP about an approved PO or a released invoice |

