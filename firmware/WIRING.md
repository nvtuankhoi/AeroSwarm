# 🔌 Cách Đấu Nối AeroSwarm (Dễ Hiểu)

> Dùng cho: ESP32-C3 + 2× TIP120 + 4 motor + RGB LED + Buzzer  
> Cách nối: xoắn dây + co nhiệt (hoặc hàn). Không cần mua thêm đồ.

---

## 1. Chuẩn Bị Trước

**Linh kiện cần nối:**
- ESP32-C3 Super Mini
- 2 con TIP120
- RGB LED (đã biết chân R, G, B, GND)
- Buzzer TMB12A05
- 4 motor 716
- Điện trở: 220Ω ×1, 100Ω ×2, 1kΩ ×2, 10kΩ ×1
- Pin Li-Po + 134N3P + cáp USB-A→C

**Dây cần cắt:**
- 6 đoạn ngắn 10cm (nối linh kiện)
- 6 đoạn Dupont cái-đầu-trần 15cm (cắm vào ESP32)
- 5 đoạn dây to 18AWG 10cm (nối 5V motor)
- 8 đoạn dây 22AWG 15cm (dây motor)

Tuốt vỏ mỗi đầu **1cm**.

---

## 2. Nối GND Chung (Đầu Tiên)

GND là mối nối lớn nhất. Tất cả đồ cần về GND đều nối vào đây.

**Xoắn chung 6 đầu dây sau:**
1. Chân GND của LED RGB (chân dài nhất)
2. Chân `−` của Buzzer (chân ngắn)
3. Chân **E** (phải) của TIP120 thứ nhất
4. Chân **E** (phải) của TIP120 thứ hai
5. **Một chân** của điện trở 10kΩ
6. Đầu trần của dây Dupont (đầu cái cắm vào chân **GND** ESP32)

> ✅ Xong GND. Để sang một bên.

---

## 3. Nối 5V Chung (Cho Motor)

**Xoắn chung 5 đầu dây to (18AWG):**
1. Dây `+` của motor 1
2. Dây `+` của motor 2
3. Dây `+` của motor 3
4. Dây `+` của motor 4
5. Đầu trần của dây Dupont (đầu cái cắm vào chân **5V** ESP32)

> ⚠️ Dùng dây to, đoạn ngắn. Nếu ESP32 bị tắt khi motor chạy, cắt cáp USB lấy dây đỏ (5V) từ 134N3P cấp trực tiếp cho motor.

---

## 4. Nối RGB LED (3 màu)

Mỗi màu = 1 điện trở nằm giữa LED và ESP32.

**Màu Đỏ:**
```
LED R ───[220Ω]───► GPIO4
```
Cách làm: xoắn chân R của LED với 1 chân 220Ω. Chân còn lại của 220Ω xoắn với dây Dupont → cắm **GPIO4**.

**Màu Xanh Lá:**
```
LED G ───[100Ω]───► GPIO5
```

**Màu Xanh Dương:**
```
LED B ───[100Ω]───► GPIO6
```

> 💡 Mỗi màu là 1 bó xoắn 2 đầu (LED + resistor), rồi xoắn thêm đầu Dupont vào resistor.

---

## 5. Nối Buzzer

```
Buzzer + ───► GPIO7
Buzzer − ───► GND chung (đã nối ở bước 2)
```

Cách làm: xoắn chân `+` Buzzer với dây Dupont → cắm **GPIO7**. Chân `−` Buzzer đã nằm trong bó GND rồi.

---

## 6. Nối 2 TIP120 Điều Khiển Motor

Mục tiêu: ESP32 ra lệnh qua GPIO10 → 2 con TIP120 bật/tắt → 4 motor quay/dừng.

Mỗi TIP120 có 3 chân: **B** (trái), **C** (giữa), **E** (phải).

---

### Bước 6.1: Xác định chân TIP120

Đặt 2 con TIP120 cạnh nhau, mặt có chữ quay lên trên, chân hướng xuống bạn:

```
      TIP120-A          TIP120-B
    ┌────────┐        ┌────────┐
    │ TIP120 │        │ TIP120 │
    └─┬─┬─┬──┘        └─┬─┬─┬──┘
      │ │ │              │ │ │
      B C E              B C E
      1 2 3              1 2 3
```

| Chân | Ký hiệu | Vị trí | Chức năng |
|------|---------|--------|-----------|
| 1 | B | Trái | Nhận lệnh từ ESP32 |
| 2 | C | Giữa | Nối vào motor âm |
| 3 | E | Phải | Nối xuống GND |

---

### Bước 6.2: Nối Emitter xuống GND

Chân **E** (phải) của **cả 2 con** đã nằm trong bó GND chung (bước 2) rồi.

Nếu chưa nối: xoắn 2 chân E vào bó GND chung gồm: LED GND, Buzzer `−`, dây Dupont → ESP32 GND.

```
TIP120-A E ──┐
             ├──► GND chung (bước 2)
TIP120-B E ──┘
```

---

### Bước 6.3: Tạo điểm BASE CHUNG

BASE CHUNG là 1 điểm xoắn dây nơi cả 2 TIP120 nhận lệnh điện.

**Lấy 5 đầu dây sau, xoắn chặt vào 1 điểm duy nhất:**

1. Chân **B** (trái) của TIP120-A
2. Chân **B** (trái) của TIP120-B
3. **1 chân** bất kỳ của điện trở 1kΩ (con thứ nhất)
4. **1 chân** bất kỳ của điện trở 1kΩ (con thứ hai)
5. **Chân còn lại** của điện trở 10kΩ (chân chưa nối GND)

> 💡 Điện trở không có chiều, nên chân nào cũng được.

Sau khi xoắn 5 đầu này, bạn có **điểm BASE CHUNG**. Còn **2 đầu dây tự do** chưa nối:
- 1 chân của điện trở 1kΩ (con thứ nhất)
- 1 chân của điện trở 1kΩ (con thứ hai)

> Điện trở 10kΩ đã dùng cả 2 chân (1 chân GND bước 2, 1 chân BASE CHUNG bước 6.3) → không còn đầu nào tự do.

---

### Bước 6.4: Nối GPIO10 vào BASE CHUNG

2 chân còn lại của 2 điện trở 1kΩ → xoắn chung với nhau → xoắn thêm dây Dupont → cắm vào **GPIO10**.

```
GPIO10 ───[1kΩ]───┬──► BASE CHUNG ──► B TIP120-A
                  │
GPIO10 ───[1kΩ]───┘──► BASE CHUNG ──► B TIP120-B
```

> Tại sao cần 1kΩ? ESP32 ra 3.3V, TIP120 cần ~1.4V để bật. 1kΩ giới hạn dòng vào Base khoảng 2mA — vừa đủ mở transistor mà không làm nóng ESP32.

---

### Bước 6.5: Kiểm tra pull-down 10kΩ

Ở bước 2, bạn đã nối **1 chân 10kΩ vào GND chung**.  
Ở bước 6.3, bạn đã nối **chân còn lại của 10kΩ vào BASE CHUNG**.

Vậy là 10kΩ đã nằm giữa BASE CHUNG và GND:

```
BASE CHUNG ───[10kΩ]───► GND chung
```

> **Tác dụng:** Khi ESP32 tắt hoặc đang khởi động, GPIO10 chưa ổn định. Điện trở 10kΩ kéo BASE xuống 0V → đảm bảo TIP120 **tắt hoàn toàn**, motor không tự quay.  
> ✅ Bạn không cần làm gì thêm ở bước này — chỉ cần xác nhận đã nối đúng 2 đầu 10kΩ.

---

### Bước 6.6: Nối Collector vào motor

**TIP120-A** điều khiển motor 1 + 2:
- Chân **C** (giữa) TIP120-A → xoắn chung với dây âm (`−`) của motor 1 và motor 2

**TIP120-B** điều khiển motor 3 + 4:
- Chân **C** (giữa) TIP120-B → xoắn chung với dây âm (`−`) của motor 3 và motor 4

```
5V ──► M1+ ── M1− ──► C TIP120-A
5V ──► M2+ ── M2− ──► C TIP120-A

5V ──► M3+ ── M3− ──► C TIP120-B
5V ──► M4+ ── M4− ──► C TIP120-B
```

---

### Bước 6.7: Kiểm tra lại từng con

| TIP120-A | Nối đâu |
|----------|---------|
| B (trái) | BASE CHUNG |
| C (giữa) | M1− + M2− |
| E (phải) | GND chung |

| TIP120-B | Nối đâu |
|----------|---------|
| B (trái) | BASE CHUNG |
| C (giữa) | M3− + M4− |
| E (phải) | GND chung |

---

## 7. Sơ Đồ Tổng Quan (Dạng Đơn Giản)

```
5V pin ───┬─── M1+ ── M1− ──[TIP120-A C]──┐
          ├─── M2+ ── M2− ─────────────────┤
          ├─── M3+ ── M3− ──[TIP120-B C]──┤
          └─── M4+ ── M4− ─────────────────┤
                                           │
GPIO10 ──[1kΩ]──┬──[1kΩ]──┬──► B TIP120-A │
                │         └──► B TIP120-B │
               [10kΩ]                   E  ├──► GND chung
                │                         E──┘
                ▼
              GND chung

GPIO4 ──[220Ω]──► LED R ──┐
GPIO5 ──[100Ω]──► LED G ──┼──► LED GND ──► GND chung
GPIO6 ──[100Ω]──► LED B ──┘

GPIO7 ─────────► Buzzer + ──┐
                            ├──► Buzzer − ──► GND chung
                            └──► (Buzzer − đã trong GND chung)

GND pin ───────► GND chung (đã có sẵn trong bó xoắn)
```

---

## 8. Test Trước Khi Co Nhiệt

1. Cắm pin Li-Po vào 134N3P.
2. Cắm cáp USB-A→C (134N3P → ESP32).
3. ESP32 nhấp nháy LED xanh onboard → OK.

**Check từng thứ:**
- LED sáng màu trắng lúc boot → OK
- Buzzer kêu 2 tiếng "bíp" → OK
- Vào dashboard, bấm **ARM**, 4 motor quay nhẹ → OK

**Nếu lỗi:**
- LED không sáng → đảo 2 đầu resistor thử (hoặc LED ngược chiều)
- Buzzer không kêu → kiểm tra chân `+`/`−`
- Motor không quay → đo lại 3 chân TIP120 (B-C-E)
- ESP32 tự tắt khi ARM → motor lấy dòng quá lớn từ pin 5V. Cắt cáp USB lấy dây đỏ từ 134N3P cấp riêng cho motor.

**OK hết thì:** trượt ống co nhiệt lên từng bó xoắn, dùng máy sấy tóc hoặc bật lửa nhẹ (cách 5cm) cho co lại.

---

## 9. Bỏ Vào Vỏ 3D

1. Nhét 4 motor vào 4 cánh, luồn dây qua kênh.
2. Đặt pin + 134N3P vào khay dưới.
3. Đặt ESP32 vào khay trên, cổng USB-C ra sau.
4. Đặt 2 TIP120 nằm ngang, lưng sát khe thông gió. **Không bọc kín transistor.**
5. Sắp xếp dây gọn, dùng keo nến cố định nếu cần.
6. Đậy nắp. Cắm prop 55mm.

---

## 10. Nhớ Flash Firmware Mới

PWM đã đổi sang 1kHz cho TIP120:
```bash
cd firmware
pio run -e demo_sysid1 -t upload
```
