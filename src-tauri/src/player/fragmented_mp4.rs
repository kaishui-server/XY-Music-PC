use std::fs::File;
use std::io::{Read, Seek, SeekFrom};

fn read_u32_be<R: Read>(reader: &mut R) -> Option<u32> {
    let mut bytes = [0_u8; 4];
    reader.read_exact(&mut bytes).ok()?;
    Some(u32::from_be_bytes(bytes))
}

fn read_u64_be<R: Read>(reader: &mut R) -> Option<u64> {
    let mut bytes = [0_u8; 8];
    reader.read_exact(&mut bytes).ok()?;
    Some(u64::from_be_bytes(bytes))
}

fn skip<R: Seek>(reader: &mut R, bytes: u64) -> Option<()> {
    reader
        .seek(SeekFrom::Current(bytes.try_into().ok()?))
        .ok()?;
    Some(())
}

fn parse_sidx_duration<R: Read + Seek>(reader: &mut R, payload_size: u64) -> Option<f64> {
    if payload_size < 24 {
        return None;
    }

    let version_and_flags = read_u32_be(reader)?;
    let version = (version_and_flags >> 24) as u8;
    let _reference_id = read_u32_be(reader)?;
    let timescale = read_u32_be(reader)?;
    if timescale == 0 {
        return None;
    }

    match version {
        0 => skip(reader, 8)?,
        1 => skip(reader, 16)?,
        _ => return None,
    }
    skip(reader, 2)?;

    let mut count_bytes = [0_u8; 2];
    reader.read_exact(&mut count_bytes).ok()?;
    let reference_count = u16::from_be_bytes(count_bytes);
    let minimum_size = if version == 0 { 24 } else { 32 };
    if payload_size < minimum_size + u64::from(reference_count) * 12 {
        return None;
    }

    let mut total_duration = 0_u64;
    for _ in 0..reference_count {
        let _reference_type_and_size = read_u32_be(reader)?;
        total_duration = total_duration.checked_add(u64::from(read_u32_be(reader)?))?;
        let _sap = read_u32_be(reader)?;
    }

    if total_duration == 0 {
        return None;
    }
    Some(total_duration as f64 / f64::from(timescale))
}

fn duration_from_sidx_reader<R: Read + Seek>(reader: &mut R) -> Option<f64> {
    let file_size = reader.seek(SeekFrom::End(0)).ok()?;
    let mut position = 0_u64;

    while position.checked_add(8)? <= file_size {
        reader.seek(SeekFrom::Start(position)).ok()?;
        let size_32 = read_u32_be(reader)?;
        let mut box_type = [0_u8; 4];
        reader.read_exact(&mut box_type).ok()?;

        let (box_size, header_size) = match size_32 {
            0 => (file_size.checked_sub(position)?, 8_u64),
            1 => (read_u64_be(reader)?, 16_u64),
            value => (u64::from(value), 8_u64),
        };
        if box_size < header_size || position.checked_add(box_size)? > file_size {
            return None;
        }

        if &box_type == b"sidx" {
            return parse_sidx_duration(reader, box_size - header_size);
        }
        position = position.checked_add(box_size)?;
    }

    None
}

/// Bilibili DASH 音频是 fragmented MP4。Symphonia 0.5 会把其中部分文件的
/// `total_duration()` 错报为约 4.29 秒，但 `sidx` 保存着所有分片的真实总时长。
pub(crate) fn duration_from_sidx_path(path: &str) -> Option<f64> {
    let mut file = File::open(path).ok()?;
    duration_from_sidx_reader(&mut file)
}

#[cfg(test)]
mod tests {
    use super::duration_from_sidx_reader;
    use std::io::Cursor;

    fn push_u32(bytes: &mut Vec<u8>, value: u32) {
        bytes.extend_from_slice(&value.to_be_bytes());
    }

    #[test]
    fn reads_total_duration_from_all_sidx_references() {
        let mut bytes = Vec::new();
        bytes.extend_from_slice(&[0, 0, 0, 12, b'f', b't', b'y', b'p', 0, 0, 0, 0]);

        let mut sidx = Vec::new();
        push_u32(&mut sidx, 0); // version + flags
        push_u32(&mut sidx, 1); // reference id
        push_u32(&mut sidx, 1_000); // timescale
        push_u32(&mut sidx, 0); // earliest presentation time
        push_u32(&mut sidx, 0); // first offset
        sidx.extend_from_slice(&0_u16.to_be_bytes()); // reserved
        sidx.extend_from_slice(&2_u16.to_be_bytes()); // reference count
        for duration in [5_016_u32, 4_984_u32] {
            push_u32(&mut sidx, 100); // referenced size
            push_u32(&mut sidx, duration);
            push_u32(&mut sidx, 0); // SAP
        }

        push_u32(&mut bytes, (8 + sidx.len()) as u32);
        bytes.extend_from_slice(b"sidx");
        bytes.extend_from_slice(&sidx);

        let duration = duration_from_sidx_reader(&mut Cursor::new(bytes)).unwrap();
        assert!((duration - 10.0).abs() < f64::EPSILON);
    }

    #[test]
    fn rejects_truncated_sidx_entries() {
        let mut bytes = Vec::new();
        push_u32(&mut bytes, 32);
        bytes.extend_from_slice(b"sidx");
        bytes.extend_from_slice(&[0; 24]);
        assert_eq!(duration_from_sidx_reader(&mut Cursor::new(bytes)), None);
    }
}
