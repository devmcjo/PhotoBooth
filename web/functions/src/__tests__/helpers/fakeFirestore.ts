/**
 * 테스트용 인메모리 Firestore 페이크(순수 단위 테스트 격리용).
 *
 * accounts.ts가 실제로 사용하는 Firestore 표면만 구현한다:
 *   collection(name).doc(id).{get,set,update,create,delete}
 *   collection(name).where(field,"==",value).{get, limit(n).get}
 *   runTransaction(async tx => { tx.get(ref|query), tx.update(ref, patch) })
 *
 * Admin SDK/네트워크에 의존하지 않는다. firebase-admin의 Timestamp만 실제 사용(문서 필드 정합).
 * 이 페이크는 jest.mock("../firebase")에서 db()가 반환하도록 주입한다.
 */

/** 문서 데이터(임의 필드). accounts는 UserDoc/TokenDoc를 저장한다. */
export type DocData = Record<string, unknown>;

interface Store {
  // collectionName -> (docId -> data)
  [collection: string]: Map<string, DocData>;
}

class FakeDocSnapshot {
  constructor(
    public readonly id: string,
    private readonly _data: DocData | undefined
  ) {}
  get exists(): boolean {
    return this._data !== undefined;
  }
  data(): DocData | undefined {
    // Firestore는 새 객체를 반환(불변). 얕은 복사로 흉내.
    return this._data ? { ...this._data } : undefined;
  }
}

class FakeQuerySnapshot {
  constructor(public readonly docs: FakeDocSnapshot[]) {}
  get empty(): boolean {
    return this.docs.length === 0;
  }
}

class FakeDocRef {
  constructor(
    private readonly store: Store,
    public readonly collectionName: string,
    public readonly id: string
  ) {}

  /** 트랜잭션 등 동기 경로에서 store를 직접 조작하기 위한 접근자. */
  map(): Map<string, DocData> {
    if (!this.store[this.collectionName]) {
      this.store[this.collectionName] = new Map();
    }
    return this.store[this.collectionName];
  }

  async get(): Promise<FakeDocSnapshot> {
    return new FakeDocSnapshot(this.id, this.map().get(this.id));
  }

  async set(data: DocData): Promise<void> {
    this.map().set(this.id, { ...data });
  }

  async create(data: DocData): Promise<void> {
    if (this.map().has(this.id)) {
      const err = new Error(`ALREADY_EXISTS: ${this.collectionName}/${this.id}`);
      err.name = "AlreadyExists";
      throw err;
    }
    this.map().set(this.id, { ...data });
  }

  async update(patch: DocData): Promise<void> {
    const cur = this.map().get(this.id);
    if (!cur) {
      throw new Error(`NOT_FOUND for update: ${this.collectionName}/${this.id}`);
    }
    this.map().set(this.id, { ...cur, ...patch });
  }

  async delete(): Promise<void> {
    this.map().delete(this.id);
  }
}

class FakeQuery {
  constructor(
    private readonly store: Store,
    private readonly collectionName: string,
    private readonly field: string,
    private readonly value: unknown,
    private readonly limitN?: number
  ) {}

  limit(n: number): FakeQuery {
    return new FakeQuery(this.store, this.collectionName, this.field, this.value, n);
  }

  private results(): FakeDocSnapshot[] {
    const map = this.store[this.collectionName] ?? new Map<string, DocData>();
    let docs = [...map.entries()]
      .filter(([, data]) => data[this.field] === this.value)
      .map(([id, data]) => new FakeDocSnapshot(id, data));
    if (this.limitN !== undefined) docs = docs.slice(0, this.limitN);
    return docs;
  }

  async get(): Promise<FakeQuerySnapshot> {
    return new FakeQuerySnapshot(this.results());
  }
}

class FakeCollectionRef {
  constructor(
    private readonly store: Store,
    private readonly collectionName: string
  ) {}

  doc(id: string): FakeDocRef {
    return new FakeDocRef(this.store, this.collectionName, id);
  }

  where(field: string, op: string, value: unknown): FakeQuery {
    if (op !== "==") throw new Error(`FakeQuery: 지원하지 않는 연산자 ${op}`);
    return new FakeQuery(this.store, this.collectionName, field, value);
  }
}

/** 트랜잭션 컨텍스트: 이 페이크는 격리 없이 store를 동기적으로 즉시 반영(테스트 목적상 충분). */
class FakeTransaction {
  async get(target: FakeDocRef | FakeQuery): Promise<FakeDocSnapshot | FakeQuerySnapshot> {
    return target.get();
  }
  update(ref: FakeDocRef, patch: DocData): void {
    // 동기 반영(fire-and-forget 회피): store를 직접 갱신.
    const map = ref.map();
    const cur = map.get(ref.id);
    if (!cur) throw new Error(`NOT_FOUND for tx.update: ${ref.collectionName}/${ref.id}`);
    map.set(ref.id, { ...cur, ...patch });
  }
  set(ref: FakeDocRef, data: DocData): void {
    ref.map().set(ref.id, { ...data });
  }
  delete(ref: FakeDocRef): void {
    ref.map().delete(ref.id);
  }
}

/** accounts.ts의 db() 반환형과 호환되는 최소 Firestore 페이크. */
export class FakeFirestore {
  private readonly store: Store = {};

  collection(name: string): FakeCollectionRef {
    return new FakeCollectionRef(this.store, name);
  }

  async runTransaction<T>(fn: (tx: FakeTransaction) => Promise<T>): Promise<T> {
    return fn(new FakeTransaction());
  }

  /** 테스트 편의: 컬렉션에 문서를 직접 심는다. */
  seed(collection: string, id: string, data: DocData): void {
    if (!this.store[collection]) this.store[collection] = new Map();
    this.store[collection].set(id, { ...data });
  }

  /** 테스트 편의: 문서 조회(단언용). */
  peek(collection: string, id: string): DocData | undefined {
    const d = this.store[collection]?.get(id);
    return d ? { ...d } : undefined;
  }

  /** 테스트 편의: 컬렉션 전체 반환(단언용). */
  all(collection: string): Array<{ id: string; data: DocData }> {
    const map = this.store[collection] ?? new Map<string, DocData>();
    return [...map.entries()].map(([id, data]) => ({ id, data: { ...data } }));
  }
}
