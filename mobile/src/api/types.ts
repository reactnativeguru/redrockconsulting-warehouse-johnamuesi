// DTOs mirroring the .NET API contracts. (In a bigger codebase, generate these
// from the services' OpenAPI documents at /openapi/v1.json, e.g. with openapi-typescript.)

export interface Product {
  sku: string;
  name: string;
  description: string;
  category: string;
  price: number;
  barcode: string;
  colours: string[];
  sizes: string[];
  attributes: Record<string, string>;
}

export interface StockDto {
  sku: string;
  onHand: number;
  reserved: number;
  available: number;
  reorderThreshold: number;
  isLow: boolean;
  lastCountedAt: string | null;
  /** SQL rowversion as a string - 64-bit, too big for a JS number. */
  version: string;
}

export interface PullResponse {
  items: StockDto[];
  checkpoint: string;
  hasMore: boolean;
}

export interface SyncMutationDto {
  mutationId: string;
  type: 'StockCount' | 'StockAdjustment';
  sku: string;
  quantity: number;
  reason?: string;
  occurredAt: string;
}

export interface PushRequest {
  storeId: string;
  deviceId: string;
  mutations: SyncMutationDto[];
}

export interface PushResponse {
  accepted: string[];
  rejected: { mutationId: string; reason: string }[];
}

export interface OrderLine {
  sku: string;
  productName: string;
  quantity: number;
  unitPrice: number;
}

export interface Order {
  id: string;
  storeId: string;
  customerName: string;
  status: 'Submitted' | 'ReadyToPick' | 'Rejected' | 'ReadyForCollection' | 'Collected';
  statusReason: string | null;
  total: number;
  createdAt: string;
  lines: OrderLine[];
}
