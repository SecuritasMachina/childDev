package com.ackdev.childDev.bean;

import static com.ackdev.common.application.CommonOfyService.ofyCommon;

import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.logging.Logger;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Component;

import com.ackdev.childDev.jdo.JournalItem;
import com.ackdev.common.dto.CommonResponse;
import com.ackdev.store.UserStore;

@Component
public class JournalBean {

	private static final Logger LOG = Logger.getLogger(JournalBean.class.getName());

	@Autowired
	private UserStore requestUserStore;

	public CommonResponse.Status update(JournalItem journal) {
		CommonResponse.Status ret = null;
		if (journal.accountFk.equalsIgnoreCase(this.requestUserStore.getUserAccount().guid)) {
			ofyCommon().save().entity(journal).now();
			ret = new CommonResponse.Status().ok();
			ret.guid = journal.guid;
		} else {
			// TODO log spoofing attempt
			LOG.warning("spoofing attempt");
			ret = new CommonResponse.Status().fail();
		}

		return ret;
	}

	public Collection<CommonResponse.Status> update(Collection<JournalItem> journalCollection) {
		ArrayList<CommonResponse.Status> ret = new ArrayList<>();
		for (JournalItem j : journalCollection) {
			CommonResponse.Status e = this.update(j);
			ret.add(e);
		}
		return ret;
	}

	public Collection<JournalItem> getRange(Long startTS, Long endTS) {
		List<JournalItem> eList = ofyCommon().load().type(JournalItem.class)
				.filter("accountFk", requestUserStore.getUserAccount().guid).filter("enteredDate >=", startTS)
				.filter("enteredDate <=", endTS).list();// .first()
		return eList;
	}

	public JournalItem get(String guid) {
		JournalItem ret = ofyCommon().load().type(JournalItem.class).id(guid).now();
		if (ret.accountFk.equalsIgnoreCase(requestUserStore.getUserAccount().guid)) {
			return ret;
		} else {
			// TODO log spoofing attempt
		}
		return null;
	}

	public CommonResponse.Status delete(String guid) {
		CommonResponse.Status ret = null;
		JournalItem journalItem = this.get(guid);
		if (journalItem.accountFk.equalsIgnoreCase(requestUserStore.getUserAccount().guid)) {
			ofyCommon().delete().entity(journalItem).now();
			ret = new CommonResponse.Status().ok();
			ret.guid = guid;

		} else {
			// TODO log spoofing attempt
			ret = new CommonResponse.Status().fail();
		}
		return ret;
	}

	public void UT_InjectUserStore(UserStore userStore) {
		this.requestUserStore = userStore;
	}

}
